// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/*
 * The implementation here matches the one present at https://github.com/dotnet/aspnetcore/blob/88180f6f487a1222b3af8c111aa6b5f8aa278633/src/Mvc/Mvc.ViewFeatures/src/Buffers/ViewBufferTextWriter.cs
 * with the distinction that it implements a constructor that takes a concrete `MemoryPoolViewBuffer` type instead of
 * the `ViewBuffer` abstract type. Source is copied here instead of shared to avoid circular dependency issues
 * between Mvc.Razor and Components.Endpoints.
 */

using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;

namespace Microsoft.AspNetCore.Components.Endpoints;

/// <summary>
/// <para>
/// A <see cref="TextWriter"/> that is backed by a unbuffered writer (over the Response stream) and/or a
/// <see cref="MemoryPoolViewBuffer"/>
/// </para>
/// <para>
/// When <c>Flush</c> or <c>FlushAsync</c> is invoked, the writer copies all content from the buffer to
/// the writer and switches to writing to the unbuffered writer for all further write operations.
/// </para>
/// </summary>
internal sealed class ViewBufferTextWriter : TextWriter
{
    private readonly TextWriter? _inner;
    private readonly HtmlEncoder? _htmlEncoder;
    private bool _inUse;

    /// <summary>
    /// Creates a new instance of <see cref="ViewBufferTextWriter"/>.
    /// </summary>
    /// <param name="buffer">The <see cref="MemoryPoolViewBuffer"/> for buffered output.</param>
    /// <param name="encoding">The <see cref="System.Text.Encoding"/>.</param>
    public ViewBufferTextWriter(MemoryPoolViewBuffer buffer, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(encoding);

        Buffer = buffer;
        Encoding = encoding;
    }

    /// <summary>
    /// Creates a new instance of <see cref="ViewBufferTextWriter"/>.
    /// </summary>
    /// <param name="buffer">The <see cref="MemoryPoolViewBuffer"/> for buffered output.</param>
    /// <param name="encoding">The <see cref="System.Text.Encoding"/>.</param>
    /// <param name="htmlEncoder">The HTML encoder.</param>
    /// <param name="inner">
    /// The inner <see cref="TextWriter"/> to write output to when this instance is no longer buffering.
    /// </param>
    public ViewBufferTextWriter(MemoryPoolViewBuffer buffer, Encoding encoding, HtmlEncoder htmlEncoder, TextWriter inner)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(htmlEncoder);
        ArgumentNullException.ThrowIfNull(inner);

        Buffer = buffer;
        Encoding = encoding;
        _htmlEncoder = htmlEncoder;
        _inner = inner;
    }

    // The issue is that ViewBufferTextWriter doesn't solve the underlying problem, which is that
    // we need a place to write output synchronously and to be able to flush it to the response
    // from time to time (when each renderbatch is finished). The specific issue with
    // ViewBufferTextWriter is that it assumes that during FlushAsync, no other writes or calls
    // to FlushAsync will occur. But EndpointHtmlRenderer can't comply with that limitation, since
    // more renders may occur and we still need a place to write them synchronously. The whole
    // problem to be solved is providing a buffer where we can always write synchronously.
    //
    // I think the reason this didn't surface as an issue (with prerendering) before is that we
    // never called FlushAsync until the end, so it just grew a large buffer. But for streaming
    // SSR we do need to flush while more renders may be occurring in the background.
    //
    // Possible solutions:
    //  - Every time there's a call to FlushAsync, swap out the current buffer with a new one,
    //    and then start a task that awaits prior underlying flushes before doing an underlying
    //    flush on the buffer we just took out of service.
    //  - Or, swap completely to using a Pipe<char> with an unlimited write buffer. All writes
    //    go into it synchronously, and when there's a call to FlushAsync we'd:
    //    - If there's still a FlushAsync going on, no-op and complete
    //    - Otherwise, start an async loop that reads chunks and asynchronously flushes them to
    //      the underlying output, until we run out of chunks.
    //  - Or, change the semantics of rendering so that ProcessRenderQueue defers its own start
    //    if an UpdateDisplayAsync is still in progress.
    //    - This is not a good solution as it would change the times when StateHasChanged causes
    //      a render.

    /// <inheritdoc />
    public override Encoding Encoding { get; }

    /// <summary>
    /// Gets the <see cref="MemoryPoolViewBuffer"/>.
    /// </summary>
    public MemoryPoolViewBuffer Buffer { get; }

    /// <inheritdoc />
    public override void Write(char value)
    {
        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(value.ToString());
    }

    /// <inheritdoc />
    public override void Write(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer.Length - index);

        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(new string(buffer, index, count));
    }

    /// <inheritdoc />
    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(value);
    }

    /// <inheritdoc />
    public override void Write(object? value)
    {
        if (value == null)
        {
            return;
        }

        if (value is IHtmlContentContainer container)
        {
            Write(container);
        }
        else if (value is IHtmlContent htmlContent)
        {
            Write(htmlContent);
        }
        else
        {
            Write(value.ToString());
        }
    }

    /// <summary>
    /// Writes an <see cref="IHtmlContent"/> value.
    /// </summary>
    /// <param name="value">The <see cref="IHtmlContent"/> value.</param>
    public void Write(IHtmlContent? value)
    {
        if (value == null)
        {
            return;
        }

        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(value);
    }

    /// <summary>
    /// Writes an <see cref="IHtmlContentContainer"/> value.
    /// </summary>
    /// <param name="value">The <see cref="IHtmlContentContainer"/> value.</param>
    public void Write(IHtmlContentContainer? value)
    {
        if (value == null)
        {
            return;
        }

        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        value.MoveTo(Buffer);
    }

    /// <inheritdoc />
    public override void WriteLine(object? value)
    {
        if (value == null)
        {
            return;
        }

        if (value is IHtmlContentContainer container)
        {
            Write(container);
            Write(NewLine);
        }
        else if (value is IHtmlContent htmlContent)
        {
            Write(htmlContent);
            Write(NewLine);
        }
        else
        {
            Write(value.ToString());
            Write(NewLine);
        }
    }

    /// <inheritdoc />
    public override Task WriteAsync(char value)
    {
        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(value.ToString());
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task WriteAsync(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (count < 0 || (buffer.Length - index < count))
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(new string(buffer, index, count));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task WriteAsync(string? value)
    {
        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(value);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override void WriteLine()
    {
        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(NewLine);
    }

    /// <inheritdoc />
    public override void WriteLine(string? value)
    {
        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(value);
        Buffer.AppendHtml(NewLine);
    }

    /// <inheritdoc />
    public override Task WriteLineAsync(char value)
    {
        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(value.ToString());
        Buffer.AppendHtml(NewLine);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task WriteLineAsync(char[] value, int start, int offset)
    {
        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(new string(value, start, offset));
        Buffer.AppendHtml(NewLine);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task WriteLineAsync(string? value)
    {
        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(value);
        Buffer.AppendHtml(NewLine);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task WriteLineAsync()
    {
        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.AppendHtml(NewLine);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Copies the buffered content to the unbuffered writer and invokes flush on it.
    /// </summary>
    public override void Flush()
    {
        if (_inner == null || _inner is ViewBufferTextWriter)
        {
            return;
        }

        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        Buffer.WriteTo(_inner, _htmlEncoder ?? HtmlEncoder.Default);
        Buffer.Clear();

        _inner.Flush();
    }

    /// <summary>
    /// Copies the buffered content to the unbuffered writer and invokes flush on it.
    /// </summary>
    /// <returns>A <see cref="Task"/> that represents the asynchronous copy and flush operations.</returns>
    public override async Task FlushAsync()
    {
        if (_inner == null || _inner is ViewBufferTextWriter)
        {
            return;
        }

        Debug.Assert(!_inUse, $"Attempting to use {nameof(ViewBufferTextWriter)} while in use");
        _inUse = true;

        await Buffer.WriteToAsync(_inner, _htmlEncoder ?? HtmlEncoder.Default);
        Buffer.Clear();

        await _inner.FlushAsync();
        _inUse = false;
    }
}
