using IdsSharp;
using NUnit.Framework;

namespace IdsSharp.Tests
{
    /// <summary>
    /// Thin on purpose. The auditing itself is buildingSMART's — reimplementing assertions about their
    /// verdicts here would be testing their library, and pinning their exact wording would break on
    /// their next release. What is worth pinning is OUR binding to it: that the call is wired up, that
    /// a bad file comes back as an error rather than silently as Ok, and that the messages survive.
    /// </summary>
    [TestFixture]
    public sealed class IdsAuditTests
    {
        [Test]
        public void A_file_that_is_not_ids_is_reported_as_an_error_with_something_to_read()
        {
            IdsAuditResult result = IdsAudit.Check("<project><wall /></project>");

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.HasErrors, Is.True);
                Assert.That(result.Status, Is.Not.Empty);
                Assert.That(result.Describe(), Is.Not.Empty);
            });
        }

        [Test]
        public void The_audit_does_not_throw_on_rubbish()
        {
            // A checker whose first step can crash on a malformed file is one people route around.
            Assert.That(() => IdsAudit.Check("not xml at all"), Throws.Nothing);
        }
    }
}
