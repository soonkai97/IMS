using System.Net;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;

namespace Ims.Api.Tests.Tests.MasterData;

[Collection(ApiCollection.Name)]
public class CategoryAndUnitTests(ApiFixture fixture)
{
    // ---------- categories ----------

    [Fact]
    public async Task A_category_can_be_created_listed_and_renamed()
    {
        var admin = await fixture.AsAsync("Admin");
        var name = Unique.Name("Cat");

        var created = (await admin.Categories.CreateAsync(name)).Data<CategoryDto>();
        Assert.Contains((await admin.Categories.ListAsync()).Data<List<CategoryDto>>(), c => c.Id == created.Id && c.Name == name);

        var renamed = (await admin.Categories.UpdateAsync(created.Id, name + " v2")).Data<CategoryDto>();
        Assert.Equal(name + " v2", renamed.Name);
        Assert.Equal(created.Id, renamed.Id);
    }

    [Fact]
    public async Task A_duplicate_category_name_is_409_on_create_and_on_rename()
    {
        var admin = await fixture.AsAsync("Admin");
        var first = (await admin.Categories.CreateAsync(Unique.Name("Cat"))).Data<CategoryDto>();
        var second = (await admin.Categories.CreateAsync(Unique.Name("Cat"))).Data<CategoryDto>();

        (await admin.Categories.CreateAsync(first.Name)).ShouldBeProblem(HttpStatusCode.Conflict);
        (await admin.Categories.UpdateAsync(second.Id, first.Name)).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_category_without_a_name_is_400_and_a_missing_category_is_404()
    {
        var admin = await fixture.AsAsync("Admin");

        Assert.NotEmpty((await admin.Categories.CreateAsync("")).ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("Name"));
        (await admin.Categories.UpdateAsync(Guid.NewGuid(), "x")).ShouldBeProblem(HttpStatusCode.NotFound);
    }

    // ---------- units ----------

    [Fact]
    public async Task A_unit_code_is_stored_in_upper_case_and_the_unit_can_be_renamed()
    {
        var admin = await fixture.AsAsync("Admin");
        var code = Unique.Code("u");

        var created = (await admin.Units.CreateAsync(code, "Test unit")).Data<UomDto>();
        Assert.Equal(code.ToUpperInvariant(), created.Code);
        Assert.Contains((await admin.Units.ListAsync()).Data<List<UomDto>>(), u => u.Id == created.Id);

        var updated = (await admin.Units.UpdateAsync(created.Id, created.Code, "Renamed")).Data<UomDto>();
        Assert.Equal("Renamed", updated.Name);
    }

    [Fact]
    public async Task A_duplicate_unit_code_is_409_whatever_its_case()
    {
        var admin = await fixture.AsAsync("Admin");
        var created = (await admin.Units.CreateAsync(Unique.Code("U"), "First")).Data<UomDto>();
        var other = (await admin.Units.CreateAsync(Unique.Code("U"), "Second")).Data<UomDto>();

        (await admin.Units.CreateAsync(created.Code.ToLowerInvariant(), "Again")).ShouldBeProblem(HttpStatusCode.Conflict);
        (await admin.Units.UpdateAsync(other.Id, created.Code, "Clash")).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_unit_without_a_code_is_400_and_a_missing_unit_is_404()
    {
        var admin = await fixture.AsAsync("Admin");

        Assert.NotEmpty((await admin.Units.CreateAsync("", "No code")).ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("Code"));
        Assert.NotEmpty((await admin.Units.CreateAsync(new string('X', 21), "Too long")).ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("Code"));
        (await admin.Units.UpdateAsync(Guid.NewGuid(), "X", "x")).ShouldBeProblem(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_seeded_units_exist()
    {
        var admin = await fixture.AsAsync("Admin");

        var codes = (await admin.Units.ListAsync()).Data<List<UomDto>>().Select(u => u.Code).ToList();

        Assert.Subset(codes.ToHashSet(), new HashSet<string> { "PCS", "CTN", "KG", "BOX" });
    }
}
