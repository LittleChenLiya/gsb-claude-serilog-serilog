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
/// Binds named message template parameters to a <see cref="MessageTemplate"/> and
/// returns a <see cref="TemplateBindingReceipt"/> describing the result.
/// </summary>
/// <remarks>
/// <para>Repeated property names in the template share a single binding: the first
/// occurrence of a name captures the corresponding value.</para>
/// <para>Values are captured lazily as scalars; <see cref="object.ToString"/> is not
/// called during binding and is evaluated only when the bound template is rendered.</para>
/// <para>Only named parameters are supported. Positional templates, and new
/// destructuring or stringify policies, are out of scope: tokens carrying
/// destructuring hints are converted with the existing default policies.</para>
/// </remarks>
public static class TemplateBinder
{
    static readonly MessageTemplateCache CachedParser = new(new MessageTemplateParser());

    // Used only for tokens carrying destructuring/stringify hints; plain named
    // values are captured lazily and never pass through the converter.
    static readonly PropertyValueConverter ValueConverter = new(
        maximumDestructuringDepth: 10,
        maximumStringLength: int.MaxValue,
        maximumCollectionCount: int.MaxValue,
        additionalScalarTypes: [],
        additionalDictionaryTypes: [],
        additionalDestructuringPolicies: [],
        propagateExceptions: false);

    /// <summary>
    /// Bind the supplied values to the named properties of a message template.
    /// </summary>
    /// <param name="messageTemplate">The template that the values apply to.</param>
    /// <param name="propertyValues">Objects corresponding to the named properties
    /// represented in the message template, in order of first occurrence.</param>
    /// <returns>A receipt describing the captured properties, any missing values,
    /// and any unmatched extra values.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="messageTemplate"/> is <code>null</code></exception>
    /// <exception cref="ArgumentNullException">When <paramref name="propertyValues"/> is <code>null</code></exception>
    /// <exception cref="ArgumentException">When the template uses positional properties.</exception>
    [MessageTemplateFormatMethod("messageTemplate")]
    public static TemplateBindingReceipt Bind(string messageTemplate, params object?[] propertyValues)
    {
        Guard.AgainstNull(messageTemplate);
        return Bind(CachedParser.Parse(messageTemplate), propertyValues);
    }

    /// <summary>
    /// Bind the supplied values to the named properties of a parsed message template.
    /// </summary>
    /// <param name="messageTemplate">The parsed template that the values apply to.</param>
    /// <param name="propertyValues">Objects corresponding to the named properties
    /// represented in the message template, in order of first occurrence.</param>
    /// <returns>A receipt describing the captured properties, any missing values,
    /// and any unmatched extra values.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="messageTemplate"/> is <code>null</code></exception>
    /// <exception cref="ArgumentNullException">When <paramref name="propertyValues"/> is <code>null</code></exception>
    /// <exception cref="ArgumentException">When the template uses positional properties.</exception>
    public static TemplateBindingReceipt Bind(MessageTemplate messageTemplate, params object?[] propertyValues)
    {
        Guard.AgainstNull(messageTemplate);
        Guard.AgainstNull(propertyValues);

        if (messageTemplate.PositionalProperties != null)
            throw new ArgumentException("Template binding receipts support named properties only; positional templates are not supported.", nameof(messageTemplate));

        var namedProperties = messageTemplate.NamedProperties;
        if (namedProperties == null || namedProperties.Length == 0)
        {
            // Every supplied value is unmatched when the template has no named properties.
            return new(messageTemplate, [], [], (object?[])propertyValues.Clone());
        }

        // Repeated names share a single binding: the first occurrence of each name wins.
        var uniqueTokens = new List<PropertyToken>(namedProperties.Length);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in namedProperties)
        {
            if (seenNames.Add(token.PropertyName))
                uniqueTokens.Add(token);
        }

        var matchedCount = Math.Min(uniqueTokens.Count, propertyValues.Length);

        var properties = new LogEventProperty[matchedCount];
        for (var i = 0; i < matchedCount; ++i)
            properties[i] = new(uniqueTokens[i].PropertyName, CaptureValue(uniqueTokens[i], propertyValues[i]));

        var missingProperties = new PropertyToken[uniqueTokens.Count - matchedCount];
        for (var i = 0; i < missingProperties.Length; ++i)
            missingProperties[i] = uniqueTokens[matchedCount + i];

        var unmatchedValues = new object?[propertyValues.Length - matchedCount];
        Array.Copy(propertyValues, matchedCount, unmatchedValues, 0, unmatchedValues.Length);

        return new(messageTemplate, properties, missingProperties, unmatchedValues);
    }

    static LogEventPropertyValue CaptureValue(PropertyToken token, object? value)
    {
        if (token.Destructuring == Destructuring.Default)
        {
            // Lazy capture: the value is held as-is and ToString() runs only when the
            // bound template is actually rendered.
            return value == null ? ScalarValue.Null : new ScalarValue(value);
        }

        // Destructuring/stringify hints use the existing conversion policies; no new
        // policies are introduced here.
        return ValueConverter.CreatePropertyValue(value, token.Destructuring);
    }
}
