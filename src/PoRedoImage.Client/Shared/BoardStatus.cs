namespace PoRedoImage.Client.Shared;

/// <summary>
/// Every state a board row can be in. There are four, and the design contract this app
/// is built to says there may never be a fifth.
/// </summary>
/// <remarks>
/// <para>
/// This exists as a type rather than a CSS convention because a convention could not be
/// enforced and had already started to fray: Bulk Generate rendered the four values from
/// a switch, Studio wrote its own untyped strings with no lamp at all, and each new
/// surface was free to add a colour. Colour is the whole signalling system on a
/// departure board — steel means waiting, amber means running, white means arrived, red
/// means cancelled — and a fifth value does not extend that system, it breaks it.
/// </para>
/// <para>
/// The names are the states, not the wording. A caller supplies its own label
/// ("Ready", "Cutting the track…") through <c>StatusCell.Label</c>; what it cannot do
/// is invent a state.
/// </para>
/// </remarks>
public enum BoardStatus
{
    /// <summary>Waiting its turn. Steel, unlit.</summary>
    Queued = 0,

    /// <summary>Running right now. Amber, and the only state that lights its lamp.</summary>
    Working = 1,

    /// <summary>Finished successfully. Paint-white.</summary>
    Done = 2,

    /// <summary>Finished unsuccessfully. Cancel-red, and announced to assistive tech.</summary>
    Failed = 3,
}
