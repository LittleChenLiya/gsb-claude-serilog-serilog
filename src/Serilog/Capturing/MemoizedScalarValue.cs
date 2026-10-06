// Copyright 2013-2026 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Serilog.Capturing;

/// <summary>
/// A scalar property value that renders lazily and memoizes its default rendering, so that a
/// value bound to a repeated template token is converted to text at most once, and only when
/// output is actually produced.
/// </summary>
sealed class MemoizedScalarValue : LogEventPropertyValue
{
    readonly ScalarValue _inner;
    string? _rendered;

    public MemoizedScalarValue(object? value)
    {
        _inner = new ScalarValue(value);
    }

    /// <summary>
    /// The wrapped value, which may be <see langword="null"/>.
    /// </summary>
    public object? Value => _inner.Value;

    public override void Render(TextWriter output, string? format = null, IFormatProvider? formatProvider = null)
    {
        Guard.AgainstNull(output);

        if (format == null && formatProvider == null)
        {
            // A benign race is possible here; the rendering is deterministic so the worst
            // outcome is a duplicated computation.
            _rendered ??= RenderDefault();
            output.Write(_rendered);
            return;
        }

        _inner.Render(output, format, formatProvider);
    }

    string RenderDefault()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        _inner.Render(output);
        return output.ToString();
    }
}
