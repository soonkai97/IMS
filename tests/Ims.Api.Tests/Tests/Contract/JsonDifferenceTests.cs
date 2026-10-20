using System.Text.Json;
using Ims.Api.Tests.Support;

namespace Ims.Api.Tests.Tests.Contract;

public class JsonDifferenceTests
{
    private static string? Compare(string expected, string actual)
    {
        using var left = JsonDocument.Parse(expected);
        using var right = JsonDocument.Parse(actual);
        return JsonDifference.First(left.RootElement, right.RootElement);
    }

    [Fact]
    public void Equal_values_in_another_member_order_and_layout_do_not_differ() =>
        Assert.Null(Compare("""{"a":1,"b":[1,2],"c":{"d":null}}""", """{ "c": { "d": null }, "b": [1, 2], "a": 1 }"""));

    [Fact]
    public void Line_endings_inside_strings_do_not_count() =>
        Assert.Null(Compare("""{"d":"one\r\ntwo\r\n"}""", """{"d":"one\ntwo\n"}"""));

    [Fact]
    public void A_lone_carriage_return_still_counts() =>
        Assert.StartsWith("$.d:", Compare("""{"d":"one\rtwo"}""", """{"d":"one\ntwo"}"""));

    [Fact]
    public void Other_text_differences_name_the_path() =>
        Assert.Equal("$.a[1].b: \"x\" vs \"y\"", Compare("""{"a":[{"b":"x"},{"b":"x"}]}""", """{"a":[{"b":"x"},{"b":"y"}]}"""));

    [Fact]
    public void A_member_present_on_one_side_only_is_named() =>
        Assert.Equal("$.b: only in the live document", Compare("""{"a":1}""", """{"a":1,"b":2}"""));

    [Fact]
    public void Array_order_counts() =>
        Assert.NotNull(Compare("""{"a":[1,2]}""", """{"a":[2,1]}"""));
}
