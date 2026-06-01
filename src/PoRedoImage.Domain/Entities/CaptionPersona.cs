namespace PoRedoImage.Domain.Entities;

/// <summary>
/// A "voice" the caption-battle model uses to generate a candidate.
/// Each persona has its own system prompt, keeping the model's tone distinct across candidates.
/// </summary>
/// <remarks>
/// Idea #5 — Meme Caption Battle. Eight personas run in parallel; the user votes
/// for the winner, building a per-user "humor profile" over time.
/// </remarks>
public enum CaptionPersona
{
    /// <summary>Gen-Z / TikTok tone — lowercase, "no cap", "slay".</summary>
    GenZ,

    /// <summary>LinkedIn-safe corporate speak.</summary>
    Corporate,

    /// <summary>Surreal absurdist humor — "why is the floor lava".</summary>
    Absurdist,

    /// <summary>Pun-heavy dad joke energy.</summary>
    DadJoke,

    /// <summary>Dry, raised-eyebrow sarcasm.</summary>
    Sarcastic,

    /// <summary>Kind, supportive, heartwarming.</summary>
    Wholesome,

    /// <summary>Tech bro / startup jargon.</summary>
    TechBro,

    /// <summary>Surreal internet nonsense — "deep fried" humor.</summary>
    Surreal
}

public static class CaptionPersonaExtensions
{
    /// <summary>Human-readable name shown in the UI.</summary>
    public static string DisplayName(this CaptionPersona p) => p switch
    {
        CaptionPersona.GenZ => "Gen-Z",
        CaptionPersona.Corporate => "Corporate",
        CaptionPersona.Absurdist => "Absurdist",
        CaptionPersona.DadJoke => "Dad Joke",
        CaptionPersona.Sarcastic => "Sarcastic",
        CaptionPersona.Wholesome => "Wholesome",
        CaptionPersona.TechBro => "Tech Bro",
        CaptionPersona.Surreal => "Surreal",
        _ => p.ToString()
    };

    /// <summary>Bootstrap-icons class — used by the UI to render a tiny chip per persona.</summary>
    public static string IconClass(this CaptionPersona p) => p switch
    {
        CaptionPersona.GenZ => "bi-phone",
        CaptionPersona.Corporate => "bi-briefcase",
        CaptionPersona.Absurdist => "bi-emoji-dizzy",
        CaptionPersona.DadJoke => "bi-emoji-laughing",
        CaptionPersona.Sarcastic => "bi-emoji-neutral",
        CaptionPersona.Wholesome => "bi-heart",
        CaptionPersona.TechBro => "bi-cpu",
        CaptionPersona.Surreal => "bi-stars",
        _ => "bi-chat-dots"
    };

    /// <summary>System prompt that primes the model for this persona.</summary>
    public static string SystemPrompt(this CaptionPersona p) => p switch
    {
        CaptionPersona.GenZ => "You are a Gen-Z TikTok caption writer. Lowercase, slang, short. 'no cap', 'slay', 'ate', 'lowkey', 'bestie' energy. Under 8 words.",
        CaptionPersona.Corporate => "You write LinkedIn-safe 'corporate' meme captions. Jargon, buzzwords, hashtag energy. Under 8 words.",
        CaptionPersona.Absurdist => "You write absurdist meme captions — surreal, nonsense, unexpectedly philosophical. Under 8 words.",
        CaptionPersona.DadJoke => "You write pun-heavy dad-joke meme captions. Groan-worthy but charming. Under 8 words.",
        CaptionPersona.Sarcastic => "You write dry, raised-eyebrow sarcastic captions. Devastatingly understated. Under 8 words.",
        CaptionPersona.Wholesome => "You write kind, supportive meme captions that make people feel good. Under 8 words.",
        CaptionPersona.TechBro => "You write tech-bro / startup caption humor. 'Disrupting', '10x engineer', 'pivot'. Under 8 words.",
        CaptionPersona.Surreal => "You write deep-fried internet absurdism — random, layered, the kind of thing that gets 50k likes. Under 8 words.",
        _ => "You write a funny meme caption. Under 8 words."
    };
}
