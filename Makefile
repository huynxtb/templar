# Templar — development commands.
#
#   make            list the targets
#   make ci         everything CI does, in CI's order
#
# CONFIG=Debug overrides the configuration; TEST=<substring> narrows `make test`;
# SAMPLE=<name> picks which sample `make run` starts (`make samples` lists them).

SLN     := Templar.slnx
CONFIG  ?= Release
OUT     := artifacts
SAMPLE  ?= InMemory
TESTS   := tests/Templar.Tests
DOTNET  := dotnet
COMPOSE := docker compose -f samples/docker-compose.yml

# The compose profiles are the lowercased sample names, so `up`/`down` take the same SAMPLE=<name>
# as `run`. SAMPLE=all brings up every service.
PROFILE  = $(shell printf '%s' '$(SAMPLE)' | tr 'A-Z' 'a-z')

# Every recipe wraps a dotnet or docker command, so nothing here is a real file.
.PHONY: help restore build rebuild test test-all format format-check run samples up down down-clean pack clean ci
.DEFAULT_GOAL := help

help: ## Show this list
	@grep -hE '^[a-z-]+:.*?## ' $(MAKEFILE_LIST) \
		| awk 'BEGIN{FS=":.*?## "} {printf "  \033[36m%-13s\033[0m %s\n", $$1, $$2}'

restore: ## Restore NuGet packages
	$(DOTNET) restore $(SLN)

build: restore ## Build the solution
	$(DOTNET) build $(SLN) --configuration $(CONFIG) --no-restore

rebuild: clean build ## Clean, then build

# TEST=Upsert runs only tests whose fully-qualified name contains "Upsert".
test: build ## Run the unit suite (TEST=<substring> to filter)
	$(DOTNET) test $(SLN) --configuration $(CONFIG) --no-build \
		$(if $(TEST),--filter "FullyQualifiedName~$(TEST)",)

# The provider round-trips skip themselves unless TEMPLAR_POSTGRES / _MYSQL / _SQLSERVER /
# _ORACLE / _MONGO hold a connection string, so this differs from `make test` only when they do.
test-all: build ## Run every test, including the provider round-trips
	$(DOTNET) test $(SLN) --configuration $(CONFIG) --no-build

# There is no .editorconfig, so `dotnet format` applies SDK defaults that disagree with the
# indentation used in a few test and sample files: it currently reports 21 whitespace diffs and
# `format` would rewrite them. Neither target is a CI gate. Check the diff before committing.
format: ## Apply formatting (rewrites files — review the diff)
	$(DOTNET) format $(SLN)

format-check: ## Report formatting diffs (currently non-zero, see comment)
	$(DOTNET) format $(SLN) --verify-no-changes

run: ## Start a sample (SAMPLE=<name>, default InMemory on port 5000)
	$(DOTNET) run --project samples/Templar.Sample.$(SAMPLE)

samples: ## List the samples and the Swagger URL each opens on
	@printf "  make run SAMPLE=%-16s %s\n" \
		InMemory         "in-memory store, no database          http://localhost:5000/swagger" \
		MemoryCache      "default in-process cache, counted     http://localhost:5001/swagger" \
		DistributedCache "IDistributedCache / Redis, counted    http://localhost:5002/swagger" \
		Scriban          "loops, conditionals and tables        http://localhost:5003/swagger" \
		PostgreSql       "PostgreSQL                            http://localhost:5010/swagger" \
		MySql            "MySQL / MariaDB                       http://localhost:5011/swagger" \
		SqlServer        "SQL Server / Azure SQL                http://localhost:5012/swagger" \
		Oracle           "Oracle Database                       http://localhost:5013/swagger" \
		Mongo            "MongoDB                               http://localhost:5014/swagger"
	@echo
	@echo "  make up SAMPLE=<name> starts that sample's database first (InMemory needs none)."

up: ## Start the database a sample needs (SAMPLE=<name>, or all)
	$(COMPOSE) --profile $(PROFILE) up -d --wait
	@case "$(PROFILE)" in sqlserver|all) \
		$(COMPOSE) --profile sqlserver --profile sqlserver-init run --rm sqlserver-init ;; esac

down: ## Stop the sample databases, keeping their data
	$(COMPOSE) --profile all down

down-clean: ## Stop the sample databases and delete their data
	$(COMPOSE) --profile all down --volumes

# Produces 14 files in $(OUT): a .nupkg and a .snupkg for each of the seven packages.
# --no-build means `make build` (or `make ci`) has to have run first, which `build` guarantees.
pack: build ## Build the NuGet packages into artifacts/
	$(DOTNET) pack $(SLN) --configuration $(CONFIG) --no-build --output $(OUT) \
		-p:ContinuousIntegrationBuild=true -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg

clean: ## Remove build output and packages
	$(DOTNET) clean $(SLN) --configuration $(CONFIG) 2>/dev/null || true
	rm -rf $(OUT)

# Mirrors .github/workflows/build.yml: restore, build, test, pack. Publishing stays in CI —
# it is triggered by bumping <Version> in Directory.Build.props, never from here.
ci: test pack ## Reproduce the CI build locally
	@echo "CI sequence complete — packages in $(OUT)/"
