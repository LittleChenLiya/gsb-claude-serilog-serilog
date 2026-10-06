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
/// Binds named values to the named tokens of a message template and reports the outcome as a
/// <see cref="TemplateBindingReceipt"/>. Only named tokens participate; positional tokens,
/// destructuring (<c>@</c>) and stringification (<c>$</c>) hints are out of scope and are
/// left uninterpreted.
/// </summary>
static class TemplateBinder
{
    /// <summary>
    /// Parse <paramref name="messageTemplate"/> and bind <paramref name="values"/> to its
    /// named tokens.
    /// </summary>
    /// <param name="messageTemplate">The message template text.</param>
    /// <param name="values">The named values to bind.</param>
    /// <returns>A receipt describing the binding.</returns>
    public static TemplateBindingReceipt Bind(string messageTemplate, IReadOnlyDictionary<string, object?> values)
    {
        Guard.AgainstNull(messageTemplate);
        return Bind(new MessageTemplateParser().Parse(messageTemplate), values);
    }

    /// <summary>
    /// Bind <paramref name="values"/> to the named tokens of <paramref name="template"/>.
    /// Repeated token names share a single binding, and bound values are converted to text
    /// lazily, only when the receipt is rendered.
    /// </summary>
    /// <param name="template">The parsed message template.</param>
    /// <param name="values">The named values to bind.</param>
    /// <returns>A receipt describing the binding.</returns>
    public static TemplateBindingReceipt Bind(MessageTemplate template, IReadOnlyDictionary<string, object?> values)
    {
        Guard.AgainstNull(template);
        Guard.AgainstNull(values);

        var bound = new Dictionary<string, LogEventPropertyValue>();
        var missing = new List<PropertyToken>();
        var tokenNames = new HashSet<string>();

        if (template.NamedProperties != null)
        {
            foreach (var token in template.NamedProperties)
            {
                if (token.IsPositional)
                    continue;

                // Repeated names resolve to the same binding; only the first occurrence
                // of a missing name is reported.
                if (!tokenNames.Add(token.PropertyName))
                    continue;

                if (values.TryGetValue(token.PropertyName, out var value))
                    bound.Add(token.PropertyName, new MemoizedScalarValue(value));
                else
                    missing.Add(token);
            }
        }

        var excess = new List<string>();
        foreach (var name in values.Keys)
        {
            if (!tokenNames.Contains(name))
                excess.Add(name);
        }

        return new TemplateBindingReceipt(template, bound, missing, excess);
    }
}
