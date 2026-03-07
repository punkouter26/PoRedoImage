namespace PoRedoImage.Web.Models;

/// <summary>
/// Default Category 1 art-style transformation prompts for Bulk Generate.
/// Each prompt contains &lt;PERSON&gt; as a placeholder replaced at generation time
/// with the AI's description of the subject in the uploaded image.
/// </summary>
public static class DefaultPrompts
{
    /// <summary>Token that gets substituted with the Computer Vision description of the uploaded image.</summary>
    public const string PersonToken = "<PERSON>";

    public static readonly string[] All =
    [
        "The Renaissance Masterpiece: A high-detail oil painting in the style of the High Renaissance, utilizing dramatic chiaroscuro lighting to create deep shadows and luminous highlights. <PERSON> is reimagined through a Renaissance aesthetic, featuring rich, hand-mixed pigments, visible fine-crackle canvas texture, and meticulous glazing. The background is a soft, sfumato-blurred landscape with classical Italian architecture and a warm, golden-hour glow.",
        "The Ukiyo-e Woodblock Legend: A traditional Japanese woodblock print from the Edo period, characterized by bold, flat areas of color and elegant, flowing line work. The scene is reconstructed around <PERSON>, incorporating iconic motifs like stylized crashing waves, cherry blossoms, or Mount Fuji. The paper texture should appear aged and fibrous, with slight ink-bleed edges and a minimalist, balanced composition.",
        "The LEGO Collector's Box Art: A high-resolution commercial product photograph of an official LEGO set box. Every element of the original image is reconstructed using 3D LEGO bricks, plates, and Minifigures, with <PERSON> as the centerpiece. The lighting is bright and \"plasticky\" with realistic specular reflections on the studs. The background features a clean, professional studio gradient with the iconic yellow logo.",
        "The Cybernetic \"Neon-Soul\" Augmentation: A gritty, hyper-realistic cyberpunk transformation that adds intricate mechanical detail to <PERSON>. Integrate glowing fiber-optic cables, carbon-fiber plating, and translucent holographic interfaces. The lighting should be dominated by \"cyan and magenta\" neon hues reflecting off wet pavement, with a shallow depth of field and anamorphic lens flares.",
        "The Editorial Vogue Cover: A high-fashion magazine cover featuring sharp, professional studio lighting and \"Retouch\" skin textures. <PERSON> is styled in an avant-garde outfit with bold makeup and a powerful, high-fashion pose. The composition includes high-end typography with the \"VOGUE\" masthead across the top and editorial headlines along the sides.",
        "The Post-Apocalyptic Overgrowth: A cinematic wide shot of <PERSON>, now reimagined as a ruin 100 years after the fall of civilization. The environment is overrun with thick vines, moss, and wildflowers reclaiming the structures. Use \"god-rays\" filtering through broken windows, dusty atmospheric particles, and a moody, desaturated color grade.",
        "The 16-Bit RPG Sprite Sheet: A meticulously crafted pixel art sprite sheet in the style of a 1990s JRPG. <PERSON> is deconstructed into sprite form, featuring a 4-frame walk cycle (front, back, side), a combat idle animation, and a \"victory\" pose. Each sprite is 64x64 pixels with sharp edges, limited color depth, and expressive facial features using only a few pixels.",
        "The Ancient Egyptian Fresco: A flat, 2D mural painted onto a textured limestone wall. <PERSON> is depicted in the traditional \"Egyptian Profile\" where the head and legs are in profile but the torso is front-facing, using a palette of ochre, turquoise, and gold leaf. The image includes faint hieroglyphics and intentional weathering/cracks to simulate thousands of years of age.",
        "The Blueprint/Patent Illustration: A clean, technical schematic on aged blueprint paper with white architectural lines. <PERSON> is broken down into its mechanical components, with call-out labels, measurements in fine script, and cross-section views. The aesthetic is professional, academic, and scientific, reminiscent of early 20th-century patent filings.",
        "The Stained Glass Gothic Window: A vibrant, backlit stained glass window composed of hundreds of individual shards of colored glass held together by lead cames. <PERSON> is transformed into the central figure, with light casting colorful \"caustics\" and patterns onto a stone floor. The glass features internal bubbles and \"reedy\" textures for maximum realism."
    ];
}
