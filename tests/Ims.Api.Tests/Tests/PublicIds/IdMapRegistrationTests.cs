using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.PublicIds;

/// <summary>The id map is wired in the real host: one instance per request (so its memory lives and dies with the request), and it works through the real repository.</summary>
[Collection(ApiCollection.Name)]
public class IdMapRegistrationTests(ApiFixture fixture)
{
    [Fact]
    public async Task The_id_map_is_one_instance_per_scope_and_maps_a_seeded_row_through_the_real_wiring()
    {
        using var first = fixture.Host.Services.CreateScope();
        using var second = fixture.Host.Services.CreateScope();

        var users = first.ServiceProvider.GetRequiredService<IIdMap<User>>();
        Assert.Same(users, first.ServiceProvider.GetRequiredService<IIdMap<User>>());
        Assert.NotSame(users, second.ServiceProvider.GetRequiredService<IIdMap<User>>());
        Assert.NotSame((object)users, first.ServiceProvider.GetRequiredService<IIdMap<Warehouse>>());

        var seeded = (await first.ServiceProvider.GetRequiredService<IReadRepository<User>>().ListAsync()).First();
        var toPublic = await users.ToPublicAsync([seeded.Id]);

        Assert.Equal(seeded.PublicId, toPublic[seeded.Id]);
        Assert.Equal(seeded.Id, (await users.ToInternalAsync([seeded.PublicId]))[seeded.PublicId]);
    }
}
