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
/// Records the outcome of binding a set of named values to the named tokens of a message
/// template: which tokens received a value, which tokens were left without one, and which
/// supplied values matched no token. Repeated token names share a single binding.
/// </summary>
sealed class TemplateBindingReceipt
{
    internal TemplateBindingReceipt(
        MessageTemplate template,
        IReadOnlyDictionary<string, LogEventPropertyValue> boundProperties,
        IReadOnlyList<PropertyToken> missingTokens,
        IReadOnlyList<string> excessValues)
    {
        Template = template;
        BoundProperties = boundProperties;
        MissingTokens = missingTokens;
        ExcessValues = excessValues;
    }

    /// <summary>
    /// The template that was bound.
    /// </summary>
    public MessageTemplate Template { get; }

    /// <summary>
    /// The values bound to named template tokens, keyed by property name. Each name appears
    /// once, no matter how many times the template references it.
    /// </summary>
    public IReadOnlyDictionary<string, LogEventPropertyValue> BoundProperties { get; }

    /// <summary>
    /// The first occurrence of each named token for which no value was supplied. The tokens
    /// are identifiable by <see cref="PropertyToken.PropertyName"/> and render as their raw
    /// text (for example, <c>{name}</c>) in the rendered output.
    /// </summary>
    public IReadOnlyList<PropertyToken> MissingTokens { get; }

    /// <summary>
    /// The names of supplied values for which the template declares no named token.
    /// </summary>
    public IReadOnlyList<string> ExcessValues { get; }

    /// <summary>
    /// <see langword="true"/> when every named token received a value and every supplied
    /// value matched a token.
    /// </summary>
    public bool IsComplete => MissingTokens.Count == 0 && ExcessValues.Count == 0;

    /// <summary>
    /// Render the bound template to a string. Tokens without a bound value are emitted as
    /// their raw text.
    /// </summary>
    /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
    /// <returns>The rendered message.</returns>
    public string Render(IFormatProvider? formatProvider = null)
    {
        var output = new StringWriter(formatProvider ?? CultureInfo.InvariantCulture);
        Render(output, formatProvider);
        return output.ToString();
    }

    /// <summary>
    /// Render the bound template to <paramref name="output"/>. Tokens without a bound value
    /// are emitted as their raw text.
    /// </summary>
    /// <param name="output">The output to write to.</param>
    /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
    public void Render(TextWriter output, IFormatProvider? formatProvider = null)
    {
        Guard.AgainstNull(output);
        Template.Render(BoundProperties, output, formatProvider);
    }
}
