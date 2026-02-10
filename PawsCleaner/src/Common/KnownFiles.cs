// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
// REVIEW NEEDED IN FUTURE TO DIFFER "-{n}" AND "{n}" AND REMOVE INCORRECTLY NAMED FILES
// !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

namespace PawsCleaner.Common;

/// <summary>
/// Lists of standard osu! filenames.
/// Used to identify valid skin elements versus garbage.
/// </summary>
public static class KnownFiles
{
    /// <summary>
    /// Checks for valid skinnable elements.
    /// Strict check for images, basename check for sounds/animations.
    /// </summary>
    public static bool IsSkinnable(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();

        // Strict check for images
        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
        {
            if (SkinnableImages.Contains(filename)) return true;

            // Animation check (strips digits/hyphens)
            var nameNoExt = Path.GetFileNameWithoutExtension(filename);
            var baseName = nameNoExt.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

            if (baseName.EndsWith("-")) baseName = baseName.Substring(0, baseName.Length - 1);

            return SkinnableAnimationBases.Contains(baseName);
        }

        // Basename check for sounds
        if (ext == ".wav" || ext == ".mp3" || ext == ".ogg")
        {
            var name = Path.GetFileNameWithoutExtension(filename);

            if (SkinnableSoundNames.Contains(name)) return true;

            // Special case for comboburst-{n}
            if (name.StartsWith("comboburst-", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = name.Substring(11);
                if (int.TryParse(suffix, out _)) return true;
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// Base names for animated elements (e.g. "sliderb" matches "sliderb0.png").
    /// </summary>
    public static readonly HashSet<string> SkinnableAnimationBases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Playfield
        "play-skip",
        "hit0",
        "hit50",
        "hit100",
        "hit100k",
        "hit300",
        "hit300g",
        "hit300k",
        "scorebar-colour",

        // Hit circles
        "followpoint",

        // Slider
        "sliderfollowcircle",
        "sliderb",

        // Pippidon
        "pippidonclear",
        "pippidonfail",
        "pippidonidle",
        "pippidonkiai",

        // Hit Bursts
        "taiko-hit0",
        "taiko-hit100",
        "taiko-hit100k",
        "taiko-hit300",
        "taiko-hit300k",

        // Notes
        "taikobigcircleoverlay",
        "taikohitcircleoverlay",

        // Catcher
        "fruit-catcher-idle",
        "fruit-catcher-fail",
        "fruit-catcher-kiai",
        "fruit-ryuuta",

        // Hit Bursts
        "mania-hit0",
        "mania-hit50",
        "mania-hit100",
        "mania-hit200",
        "mania-hit300",
        "mania-hit300g",

        // Comboburst
        "comboburst",
        "comboburst-fruits",
        "comboburst-mania",
        "taiko-flower-group",
    };

    /// <summary>
    /// Sound basenames without extension.
    /// </summary>
    public static readonly HashSet<string> SkinnableSoundNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "count",
        "count1s",
        "count2s",
        "count3s",
        "gos",
        "readys",
        "comboburst",
        "combobreak",
        "failsound",
        "sectionpass",
        "sectionfail",
        "applause",
        "pause-loop",
        "drum-hitnormal",
        "drum-hitclap",
        "drum-hitfinish",
        "drum-hitwhistle",
        "drum-slidertick",
        "drum-sliderslide",
        "drum-sliderwhistle",
        "normal-hitnormal",
        "normal-hitclap",
        "normal-hitfinish",
        "normal-hitwhistle",
        "normal-slidertick",
        "normal-sliderslide",
        "normal-sliderwhistle",
        "soft-hitnormal",
        "soft-hitclap",
        "soft-hitfinish",
        "soft-hitwhistle",
        "soft-slidertick",
        "soft-sliderslide",
        "soft-sliderwhistle",
        "spinnerspin",
        "spinnerbonus",
        "nightcore-kick",
        "nightcore-clap",
        "nightcore-hat",
        "nightcore-finish",
        "taiko-normal-hitnormal",
        "taiko-normal-hitclap",
        "taiko-normal-hitfinish",
        "taiko-normal-hitwhistle",
        "taiko-soft-hitnormal",
        "taiko-soft-hitclap",
        "taiko-soft-hitfinish",
        "taiko-soft-hitwhistle",
        "taiko-drum-hitnormal",
        "taiko-drum-hitclap",
        "taiko-drum-hitfinish",
        "taiko-drum-hitwhistle"
        // "spinnerbonus-max" only used in lazer
    };

    /// <summary>
    /// Strict image filenames.
    /// </summary>
    public static readonly HashSet<string> SkinnableImages = new(StringComparer.OrdinalIgnoreCase)
    {
        // Cursor
        "cursor.png",
        "cursormiddle.png",
        "cursor-smoke.png",
        "cursortrail.png",

        // Mod icons
        "selection-mod-autoplay.png",
        "selection-mod-cinema.png",
        "selection-mod-doubletime.png",
        "selection-mod-easy.png",
        "selection-mod-fadein.png",
        "selection-mod-flashlight.png",
        "selection-mod-halftime.png",
        "selection-mod-hardrock.png",
        "selection-mod-hidden.png",
        "selection-mod-key1.png",
        "selection-mod-key2.png",
        "selection-mod-key3.png",
        "selection-mod-key4.png",
        "selection-mod-key5.png",
        "selection-mod-key6.png",
        "selection-mod-key7.png",
        "selection-mod-key8.png",
        "selection-mod-key9.png",
        "selection-mod-keycoop.png",
        "selection-mod-mirror.png",
        "selection-mod-nightcore.png",
        "selection-mod-nofail.png",
        "selection-mod-perfect.png",
        "selection-mod-random.png",
        "selection-mod-relax.png",
        "selection-mod-relax2.png",
        "selection-mod-scorev2.png",
        "selection-mod-spunout.png",
        "selection-mod-suddendeath.png",
        "selection-mod-target.png",
        "selection-mod-freemodallowed.png",
        "selection-mod-touchdevice.png",

        // Playfield
        "play-unranked.png",
        "multi-skipped.png",
        "section-fail.png",
        "section-pass.png",
        "count1.png",
        "count2.png",
        "count3.png",
        "go.png",
        "ready.png",
        "inputoverlay-background.png",
        "inputoverlay-key.png",
        "pause-overlay.png",
        "fail-background.png",
        "pause-back.png",
        "pause-continue.png",
        "pause-retry.png",
        "scorebar-bg.png",
        "scorebar-ki.png",
        "scorebar-kidanger.png",
        "scorebar-kidanger2.png",
        "scorebar-marker.png",
        "score-0.png",
        "score-1.png",
        "score-2.png",
        "score-3.png",
        "score-4.png",
        "score-5.png",
        "score-6.png",
        "score-7.png",
        "score-8.png",
        "score-9.png",
        "score-comma.png",
        "score-dot.png",
        "score-percent.png",
        "score-x.png",
        "score-pp.png",

        // Ranking grades
        "ranking-XH-small.png",
        "ranking-X-small.png",
        "ranking-SH-small.png",
        "ranking-S-small.png",
        "ranking-A-small.png",
        "ranking-B-small.png",
        "ranking-C-small.png",
        "ranking-D-small.png",

        // Score entry
        "scoreentry-0.png",
        "scoreentry-1.png",
        "scoreentry-2.png",
        "scoreentry-3.png",
        "scoreentry-4.png",
        "scoreentry-5.png",
        "scoreentry-6.png",
        "scoreentry-7.png",
        "scoreentry-8.png",
        "scoreentry-9.png",
        "scoreentry-comma.png",
        "scoreentry-dot.png",
        "scoreentry-percent.png",
        "scoreentry-x.png",

        // Song selection
        "menu-button-background.png",
        "selection-tab.png",
        "star2.png",

        // Default Numbers
        "default-0.png",
        "default-1.png",
        "default-2.png",
        "default-3.png",
        "default-4.png",
        "default-5.png",
        "default-6.png",
        "default-7.png",
        "default-8.png",
        "default-9.png",

        // Hit circles
        "approachcircle.png",
        "hitcircle.png",
        "hitcircleoverlay.png",
        "hitcircleselect.png",
        "lighting.png",

        // Slider
        "sliderstartcircle.png",
        "sliderstartcircleoverlay.png",
        "sliderendcircle.png",
        "sliderendcircleoverlay.png",
        "reversearrow.png",
        "sliderb-nd.png",
        "sliderb-spec.png",
        "sliderpoint10.png",
        "sliderpoint30.png",
        "sliderscorepoint.png",

        // Spinner
        "spinner-approachcircle.png",
        "spinner-rpm.png",
        "spinner-clear.png",
        "spinner-spin.png",
        "spinner-background.png",
        "spinner-circle.png",
        "spinner-metre.png",
        "spinner-osu.png",
        "spinner-glow.png",
        "spinner-bottom.png",
        "spinner-top.png",
        "spinner-middle2.png",
        "spinner-middle.png",

        // Particles
        "particle50.png",
        "particle100.png",
        "particle300.png",

        // Notes
        "taikobigcircle.png",
        "taikohitcircle.png",

        // Playfield (upper half)
        "taiko-slider.png",
        "taiko-flower-group.png",

        // Drumrolls
        "taiko-roll-middle.png",
        "taiko-roll-end.png",

        // Shaker
        "spinner-warning.png",

        // Fruits
        "fruit-pear.png",
        "fruit-pear-overlay.png",
        "fruit-grapes.png",
        "fruit-grapes-overlay.png",
        "fruit-apple.png",
        "fruit-apple-overlay.png",
        "fruit-orange.png",
        "fruit-orange-overlay.png",
        "fruit-bananas.png",
        "fruit-bananas-overlay.png",
        "fruit-drop.png",
        "fruit-drop-overlay.png",
    };
}
