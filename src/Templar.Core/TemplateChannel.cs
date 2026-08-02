namespace Templar;

/// <summary>
/// The delivery channel a template belongs to. The same template key can exist for
/// several channels (for example a welcome e-mail and a welcome in-app notification).
/// </summary>
public enum TemplateChannel
{
    /// <summary>An e-mail: subject plus a text and/or HTML body.</summary>
    Email = 0,

    /// <summary>An in-app notification: title (subject) plus a short body.</summary>
    InApp = 1,

    /// <summary>A text message: the plain-text body, kept short. No subject and no HTML.</summary>
    Sms = 2,

    /// <summary>A WhatsApp message.</summary>
    WhatsApp = 3,

    /// <summary>A Zalo message.</summary>
    Zalo = 4,

    /// <summary>A Facebook message — Messenger, or a page notification.</summary>
    Facebook = 5,

    /// <summary>
    /// Anything else that renders from the same template store — push payloads, a webhook body, a
    /// PDF fragment. Templar does not deliver messages, so it does not need to know what the
    /// destination is; this keeps such templates out of the named channels. Always declared last.
    /// </summary>
    Other = 6,
}
