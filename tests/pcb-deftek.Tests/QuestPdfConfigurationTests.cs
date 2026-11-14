using FluentAssertions;
using pcb_deftek.Infrastructure.Export;
using QuestPDF.Infrastructure;
using Xunit;

namespace pcb_deftek.Tests;

public class QuestPdfConfigurationTests
{
    [Fact]
    public void EnsureConfigured_SetsTheCommunityLicenseAndIsIdempotent()
    {
        QuestPdfConfiguration.EnsureConfigured();
        QuestPdfConfiguration.EnsureConfigured();

        QuestPDF.Settings.License.Should().Be(LicenseType.Community);
    }
}
