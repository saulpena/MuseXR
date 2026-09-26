using MuseXR.Worlds;
using NUnit.Framework;

namespace MusePico.Tests.EditMode
{
    public class WorldPropsTests
    {
        const string Peach = "celestial-peach-blossom-paradise-500k";

        [Test]
        public void ShowsOnlyForItsOwnWorld()
        {
            Assert.IsTrue(WorldProps.ShouldShow(Peach, Peach));
            Assert.IsFalse(WorldProps.ShouldShow("grand-conservatory-garden-path-cut-500k", Peach));
        }

        [Test]
        public void TheSizeSuffixMatters()
        {
            // The catalog keys carry -500k; a props group keyed without it would never appear.
            Assert.IsFalse(WorldProps.ShouldShow(Peach, "celestial-peach-blossom-paradise"));
        }

        [Test]
        public void NothingLoadedOrNoKeyMeansHidden()
        {
            Assert.IsFalse(WorldProps.ShouldShow(null, Peach));
            Assert.IsFalse(WorldProps.ShouldShow(Peach, null));
            Assert.IsFalse(WorldProps.ShouldShow(Peach, ""));
        }

        [Test]
        public void ThePeachPropsKeyIsARealCatalogWorld()
        {
            Assert.IsNotNull(System.Linq.Enumerable.FirstOrDefault(WorldCatalog.Small, w => w.key == Peach),
                "the props group in Museum.unity is keyed to a world the catalog must contain");
        }
    }
}
