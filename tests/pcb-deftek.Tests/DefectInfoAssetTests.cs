using System;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using FluentAssertions;
using pcb_deftek.Domain.Models;
using pcb_deftek.Presentation.Converters;
using Xunit;

namespace pcb_deftek.Tests;

public class DefectInfoAssetTests
{
    [Fact]
    public void Converter_ResolvesAllNinePackUrisWithLowercaseAbbreviations()
    {
        var converter = new DefectAbbreviationToImagePathConverter();

        foreach (var info in DefectKnowledgeBase.GetAll())
        {
            var uri = converter.Convert(info.Abbreviation, typeof(Uri), null, System.Globalization.CultureInfo.InvariantCulture)
                .Should().BeOfType<Uri>().Subject;

            uri.OriginalString.Should().Be($"/pcb-deftek;component/Assets/Images/dashb-info-{info.Abbreviation.ToLowerInvariant()}.png");
        }
    }

    [Theory]
    [InlineData("dashb-info-sh.png")]
    [InlineData("dashb-info-sp.png")]
    [InlineData("dashb-info-sc.png")]
    [InlineData("dashb-info-op.png")]
    [InlineData("dashb-info-mb.png")]
    [InlineData("dashb-info-hb.png")]
    [InlineData("dashb-info-cs.png")]
    [InlineData("dashb-info-cfo.png")]
    [InlineData("dashb-info-bmfo.png")]
    public void AllNineAssetFilesExist(string fileName)
    {
        string solutionRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        File.Exists(Path.Combine(solutionRoot, "Assets", "Images", fileName)).Should().BeTrue();
    }

    [Fact]
    public void ResolvedAssetUri_LoadsThePackagedImage()
    {
        RunOnSta(() =>
        {
            var converter = new DefectAbbreviationToImagePathConverter();
            var uri = (Uri)converter.Convert("SH", typeof(Uri), null, System.Globalization.CultureInfo.InvariantCulture)!;
            var resource = System.Windows.Application.GetResourceStream(uri);
            resource.Should().NotBeNull();
            resource!.Stream.Dispose();
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../SH")]
    [InlineData("S H")]
    public void InvalidAbbreviation_ReturnsNull(string? abbreviation)
    {
        var converter = new DefectAbbreviationToImagePathConverter();

        converter.Convert(abbreviation, typeof(Uri), null, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeNull();
    }

    private static void RunOnSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (captured is not null)
        {
            throw new InvalidOperationException("STA test execution failed.", captured);
        }
    }
}
