// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace Microsoft.AspNetCore.Components.Endpoints.Rendering;

public class TextChunkTest
{
    private StringWriter _writer = new();
    private StringBuilder _tempBuffer;

    [Fact]
    public async Task CanHoldString()
    {
        var chunk = new TextChunk("string value");
        await chunk.WriteToAsync(_writer, ref _tempBuffer);
        Assert.Equal("string value", _writer.ToString());
    }

    [Fact]
    public async Task CanHoldChar()
    {
        var chunk = new TextChunk('x');
        await chunk.WriteToAsync(_writer, ref _tempBuffer);
        Assert.Equal("x", _writer.ToString());
    }

    [Fact]
    public async Task CanHoldCharArraySegment()
    {
        var chars = new char[] { 'a', 'b', 'c', 'd', 'e' };
        var chunk = new TextChunk(new ArraySegment<char>(chars, 1, 3));
        await chunk.WriteToAsync(_writer, ref _tempBuffer);
        Assert.Equal("bcd", _writer.ToString());
    }

    [Fact]
    public async Task CanHoldInt()
    {
        await new TextChunk(123).WriteToAsync(_writer, ref _tempBuffer);
        await new TextChunk(456).WriteToAsync(_writer, ref _tempBuffer);
        Assert.Equal("123456", _writer.ToString());
    }
}