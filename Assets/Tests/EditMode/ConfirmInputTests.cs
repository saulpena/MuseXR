using MuseXR.Interaction;
using NUnit.Framework;

namespace MusePico.Tests
{
    public class ConfirmInputTests
    {
        sealed class Holder : IConfirmable
        {
            public int Confirms;
            public bool Confirm() { Confirms++; return true; }
            public bool Redo() => false;
        }

        [Test]
        public void ARetunsToTheEarlierHolderWhenTheLatestLetsGo()
        {
            var lanterns = new Holder(); var round = new Holder();
            try
            {
                ConfirmInput.Take(lanterns);
                ConfirmInput.Take(round);          // a master's round takes A
                Assert.AreSame(round, ConfirmInput.Focus);
                ConfirmInput.Drop(round);          // and lets go when it ends
                Assert.AreSame(lanterns, ConfirmInput.Focus, "A goes back to the lanterns, not to nobody");
                ConfirmInput.PressA();
                Assert.AreEqual(1, lanterns.Confirms);
            }
            finally { ConfirmInput.Drop(lanterns); ConfirmInput.Drop(round); }
        }

        [Test]
        public void TakingAgainMovesAHolderToTheTop()
        {
            var a = new Holder(); var b = new Holder();
            try
            {
                ConfirmInput.Take(a); ConfirmInput.Take(b); ConfirmInput.Take(a);
                Assert.AreSame(a, ConfirmInput.Focus);
                ConfirmInput.Drop(a);
                Assert.AreSame(b, ConfirmInput.Focus);
            }
            finally { ConfirmInput.Drop(a); ConfirmInput.Drop(b); }
        }
    }
}
