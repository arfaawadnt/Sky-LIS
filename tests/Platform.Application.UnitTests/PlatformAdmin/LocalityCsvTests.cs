using System.Text;
using Platform.Application.PlatformAdmin;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.UnitTests.PlatformAdmin;

/// <summary>LIS-CR-008 C-04 (D-2): the locality file of SA-11 "Import CSV" (intake LIS-INT-PLT-001 §22).</summary>
public sealed class LocalityCsvTests
{
    [Fact]
    public void The_bilingual_header_is_read_in_any_order_with_quotes_and_blank_lines()
    {
        var rows = LocalityCsv.Parse("\uFEFFdistrict_en,city_en,city_ar,area_en\r\n\"Zone, First\",Nasr City,مدينة نصر,Block 7\r\n\r\n,Maadi,,\r\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(new LocalityImportRow(2, "Nasr City", "مدينة نصر", "Zone, First", null, "Block 7", null), rows[0]);
        Assert.Equal(new LocalityImportRow(4, "Maadi", null, null, null, null, null), rows[1]);
    }

    [Fact]
    public void Short_column_names_semicolons_and_village_are_accepted()
    {
        var row = Assert.Single(LocalityCsv.Parse("City;District;Village;village_ar\nNasr City;Zone 1;Kafr;كفر\n"));

        Assert.Equal(new LocalityImportRow(2, "Nasr City", null, "Zone 1", null, "Kafr", "كفر"), row);
    }

    [Theory]
    [InlineData("")]
    [InlineData("name,code\nA,B\n")]
    [InlineData("city_en\n")]
    [InlineData("city_en\n\"Nasr City\n")]
    public void An_unreadable_file_is_GEO_023(string text)
    {
        var refusal = Assert.Throws<DomainException>(() => LocalityCsv.Parse(text));

        Assert.Equal((PlatformAdminCodes.LocalityImportFileInvalid, DomainFault.Validation), (refusal.Code, refusal.Fault));
    }

    [Fact]
    public void More_lines_than_the_limit_is_GEO_024()
    {
        var text = new StringBuilder("city_en\n");
        for (var i = 0; i <= LocalityCsv.MaxRows; i++)
        {
            text.Append("City ").Append(i).Append('\n');
        }

        Assert.Equal(PlatformAdminCodes.LocalityImportTooLarge, Assert.Throws<DomainException>(() => LocalityCsv.Parse(text.ToString())).Code);
    }

    [Fact]
    public async Task The_stream_is_read_as_UTF_8()
    {
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("city_ar\nمدينة نصر\n"));

        var row = Assert.Single(await LocalityCsv.ReadAsync(content, CancellationToken.None));

        Assert.Equal("مدينة نصر", row.CityAr);
    }
}
