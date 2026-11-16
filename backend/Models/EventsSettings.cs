namespace AltusIQ.Api.Models;

public class EventsSettings
{
    /// <summary>
    /// 7500 events are hidden from the public API unless this is on. Default
    /// off: the code means unlawful interference and is not demo material.
    /// </summary>
    public bool ExposeSensitive { get; set; } = false;

    /// <summary>Open events are handed to the investigator after this long even if still open.</summary>
    public int InvestigateOpenAfterMinutes { get; set; } = 30;

    /// <summary>Evidence snapshots are thinned to at most this many track points.</summary>
    public int MaxEvidencePoints { get; set; } = 120;
}
