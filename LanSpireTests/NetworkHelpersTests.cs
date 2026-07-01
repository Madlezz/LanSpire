using Xunit;
using LanSpire.Helpers;
using LanSpire.Patches;

namespace LanSpireTests;

public class NetworkHelpersTests
{
    [Theory]
    [InlineData("192.168.1.1", "33771", "192.168.1.1", 33771)]
    [InlineData("10.0.0.5", "8080", "10.0.0.5", 8080)]
    [InlineData("172.16.0.1", "65535", "172.16.0.1", 65535)]
    public void TryParseEndpoint_ValidHostAndPort_ReturnsTrue(
        string hostInput, string portInput, string expectedHost, ushort expectedPort)
    {
        var result = NetworkHelpers.TryParseEndpoint(hostInput, portInput,
            out var host, out var port, out var error);

        Assert.True(result);
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData("192.168.1.1:9000", "", "192.168.1.1", 9000)]
    [InlineData("10.0.0.5:443", "", "10.0.0.5", 443)]
    [InlineData("172.16.0.1:33771", "", "172.16.0.1", 33771)]
    public void TryParseEndpoint_InlinePort_ExtractsPort(
        string input, string portInput, string expectedHost, ushort expectedPort)
    {
        var result = NetworkHelpers.TryParseEndpoint(input, portInput,
            out var host, out var port, out var error);

        Assert.True(result);
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
    }

    [Theory]
    [InlineData("", "33771")]
    [InlineData("   ", "33771")]
    public void TryParseEndpoint_EmptyHost_ReturnsFalse(string hostInput, string portInput)
    {
        var result = NetworkHelpers.TryParseEndpoint(hostInput, portInput,
            out var host, out var port, out var error);

        Assert.False(result);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("192.168.1.1", "0")]
    [InlineData("192.168.1.1", "abc")]
    [InlineData("192.168.1.1", "99999")]
    [InlineData("192.168.1.1", "-1")]
    public void TryParseEndpoint_InvalidPort_ReturnsFalse(string hostInput, string portInput)
    {
        var result = NetworkHelpers.TryParseEndpoint(hostInput, portInput,
            out var host, out var port, out var error);

        Assert.False(result);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryParseEndpoint_EmptyPort_UsesDefaultPort()
    {
        var result = NetworkHelpers.TryParseEndpoint("192.168.1.1", "",
            out var host, out var port, out var error);

        Assert.True(result);
        Assert.Equal("192.168.1.1", host);
        Assert.Equal(LanMultiplayerPatches.DefaultPort, port);
    }

    [Theory]
    [InlineData("192.168.1.1:8080", "192.168.1.1", 8080)]
    [InlineData("10.0.0.5:443", "10.0.0.5", 443)]
    [InlineData("172.16.0.1:33771", "172.16.0.1", 33771)]
    public void TryExtractInlinePort_Valid_ReturnsTrue(
        string input, string expectedHost, ushort expectedPort)
    {
        var host = input;
        var result = NetworkHelpers.TryExtractInlinePort(ref host, out var port);

        Assert.True(result);
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("no_colon_here")]
    [InlineData(":8080")]
    [InlineData("192.168.1.1:")]
    [InlineData("192.168.1.1:abc")]
    [InlineData("192.168.1.1:0")]
    [InlineData("192.168.1.1:99999")]
    public void TryExtractInlinePort_Invalid_ReturnsFalse(string input)
    {
        var host = input;
        var result = NetworkHelpers.TryExtractInlinePort(ref host, out var port);

        Assert.False(result);
    }

    [Theory]
    [InlineData("hello world", "hello world")]
    [InlineData("  trim me  ", "trim me")]
    [InlineData("", "")]
    public void NormalizeText_VariousInputs_ReturnsTrimmed(string input, string expected)
    {
        var result = NetworkHelpers.NormalizeText(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeText_NullInput_ReturnsEmpty()
    {
        var result = NetworkHelpers.NormalizeText(null!);
        Assert.Equal(string.Empty, result);
    }
}

