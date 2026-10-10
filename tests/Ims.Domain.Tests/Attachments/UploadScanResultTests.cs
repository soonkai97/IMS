using Ims.Application.Features.Attachments;

namespace Ims.Domain.Tests.Attachments;

public class UploadScanResultTests
{
    [Fact]
    public void Clean_is_clean_and_has_no_reason()
    {
        Assert.True(UploadScanResult.Clean.IsClean);
        Assert.Null(UploadScanResult.Clean.Reason);
    }

    [Fact]
    public void Infected_is_not_clean_and_keeps_the_reason_without_control_characters()
    {
        var result = UploadScanResult.Infected("Eicar-Test\r\n2026-01-01 [INF] forged line\t");

        Assert.False(result.IsClean);
        Assert.Equal("Eicar-Test2026-01-01 [INF] forged line", result.Reason);
    }

    [Fact]
    public void A_very_long_reason_is_cut_so_it_cannot_flood_the_log()
    {
        Assert.Equal(200, UploadScanResult.Infected(new string('x', 5000)).Reason!.Length);
    }
}
