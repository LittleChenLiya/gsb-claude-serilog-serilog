// Copyright © Serilog Contributors
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
/// Records the outcome of binding named message template parameters to a
/// <see cref="MessageTemplate"/>: the captured properties, the named tokens
/// left without a value, and the supplied values left without a matching token.
/// </summary>
/// <remarks>
/// Repeated property names in the template share a single binding: the first
/// occurrence of a name captures the value, and later occurrences of the same
/// name render the same captured value. Values are captured lazily, so
/// <see cref="object.ToString"/> is not called during binding; it is evaluated
/// only when the bound template is actually rendered, and only once per unique
/// token within a single call to <see cref="Render(TextWriter, IFormatProvider?)"/>.
/// </remarks>
public sealed class TemplateBindingReceipt
{
    readonly LogEventProperty[] _properties;
    readonly PropertyToken[] _missingProperties;
    readonly object?[] _unmatchedValues;

    IReadOnlyDictionary<string, LogEventPropertyValue>? _propertiesByName;

    internal TemplateBindingReceipt(
        MessageTemplate template,
        LogEventProperty[] properties,
        PropertyToken[] missingProperties,
        object?[] unmatchedValues)
    {
        Template = template;
        _properties = properties;
        _missingProperties = missingProperties;
        _unmatchedValues = unmatchedValues;
    }

    /// <summary>
    /// The template that the parameters were bound to.
    /// </summary>
    public MessageTemplate Template { get; }

    /// <summary>
    /// The properties captured from the supplied values. Repeated property names
    /// in the template appear only once; the first occurrence performs the capture.
    /// </summary>
    public IReadOnlyList<LogEventProperty> Properties => _properties;

    /// <summary>
    /// The named tokens for which no value was supplied. Each missing value can be
    /// identified by the corresponding token's <see cref="PropertyToken.PropertyName"/>.
    /// </summary>
    public IReadOnlyList<PropertyToken> MissingProperties => _missingProperties;

    /// <summary>
    /// The supplied values for which the template has no matching named token.
    /// </summary>
    public IReadOnlyList<object?> UnmatchedValues => _unmatchedValues;

    /// <summary>
    /// True when every named token received a value and every supplied value
    /// matched a named token.
    /// </summary>
    public bool IsComplete => _missingProperties.Length == 0 && _unmatchedValues.Length == 0;

    /// <summary>
    /// Render the bound template to a string.
    /// </summary>
    /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
    /// <returns>The rendered message.</returns>
    public string Render(IFormatProvider? formatProvider = null)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        Render(output, formatProvider);
        return output.ToString();
    }

    /// <summary>
    /// Render the bound template to <paramref name="output"/>. Tokens with missing
    /// values are written verbatim, as their raw text. Repeated occurrences of the
    /// same token are evaluated only once and the same rendered text is written for
    /// each occurrence.
    /// </summary>
    /// <param name="output">Output for the rendered string.</param>
    /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="output"/> is <code>null</code></exception>
    public void Render(TextWriter output, IFormatProvider? formatProvider = null)
    {
        Guard.AgainstNull(output);

        var properties = _propertiesByName ??= CreatePropertiesByName(_properties);
        Dictionary<string, string>? renderedTokens = null;

        foreach (var token in Template.TokenArray)
        {
            if (token is not PropertyToken propertyToken)
            {
                token.Render(properties, output, formatProvider);
                continue;
            }

            // Repeated occurrences of the same token share a single binding: evaluate
            // the captured value (including any ToString() call) only once.
            renderedTokens ??= [];
            if (!renderedTokens.TryGetValue(propertyToken.RawText, out var rendered))
            {
                var tokenOutput = new StringWriter(CultureInfo.InvariantCulture);
                propertyToken.Render(properties, tokenOutput, formatProvider);
                rendered = tokenOutput.ToString();
                renderedTokens.Add(propertyToken.RawText, rendered);
            }

            output.Write(rendered);
        }
    }

    /// <summary>
    /// Create a <see cref="LogEvent"/> carrying the bound template and properties, so
    /// that the receipt can be emitted through the regular <see cref="ILogger"/> pipeline.
    /// </summary>
    /// <param name="level">The level of the event.</param>
    /// <param name="exception">An exception associated with the event, or null.</param>
    /// <returns>A log event with the bound template and properties.</returns>
    public LogEvent ToLogEvent(LogEventLevel level = LogEventLevel.Information, Exception? exception = null)
    {
        return new(DateTimeOffset.Now, level, exception, Template, _properties);
    }

    static IReadOnlyDictionary<string, LogEventPropertyValue> CreatePropertiesByName(LogEventProperty[] properties)
    {
        var result = new Dictionary<string, LogEventPropertyValue>(properties.Length);
        foreach (var property in properties)
            result[property.Name] = property.Value;
        return result;
    }
}
