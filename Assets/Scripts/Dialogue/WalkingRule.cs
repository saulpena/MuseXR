namespace MusePico.Dialogue
{
    /// <summary>
    /// When the visitor may walk. Engine-free so it is testable; the runner applies it to the
    /// rig's locomotion subtree and CharacterController together.
    /// </summary>
    public static class WalkingRule
    {
        /// <summary>The stages the visitor walks. Hers: only <c>world_exploration</c>.</summary>
        public static bool Walks(Stage stage) => stage == Stage.WorldExploration;

        /// <summary>
        /// Locomotion and gravity together: only on a floor that exists (a world with no floor
        /// drops the rig out of it), and only in the gallery stage unless free walking was asked
        /// for at launch (<c>musexr.freeWalk</c>), which opens every stage to walking.
        /// </summary>
        public static bool Allowed(Stage stage, bool freeWalk, bool floorReady) =>
            floorReady && (Walks(stage) || freeWalk);
    }
}
