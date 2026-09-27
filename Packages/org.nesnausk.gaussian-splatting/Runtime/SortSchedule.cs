namespace GaussianSplatting.Runtime
{
    /// <summary>
    /// MuseXR patch: when a splat renderer re-sorts. Pure, so it is EditMode-testable.
    ///
    /// The sorted order is a view-dependent result stored in one buffer per renderer, so it is only
    /// valid for the camera that produced it. Skipping sorts (<c>m_SortNthFrame</c> above 1) is a
    /// sound saving only while the SAME camera keeps rendering; a different camera must sort first.
    /// </summary>
    public static class SortSchedule
    {
        public static bool ShouldSort(int frameCounter, int sortNthFrame, bool sameCameraAsLastSort)
        {
            if (!sameCameraAsLastSort) return true;
            if (sortNthFrame <= 1) return true;
            return frameCounter % sortNthFrame == 0;
        }
    }
}
