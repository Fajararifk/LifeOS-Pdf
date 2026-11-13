using System;
using System.Collections.Generic;

namespace LifeOS.Pdf.Templates.Expressions
{
    /// <summary>Variables visible while binding: the data root plus loop variables (innermost first).</summary>
    internal sealed class Scope
    {
        private readonly Dictionary<string, object?> _variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        private readonly Scope? _parent;

        public Scope(object? root, TemplateOptions options)
        {
            Root = root;
            Options = options;
        }

        private Scope(Scope parent)
        {
            _parent = parent;
            Root = parent.Root;
            Options = parent.Options;
        }

        public object? Root { get; }

        public TemplateOptions Options { get; }

        public Scope With(string name, object? value)
        {
            var child = new Scope(this);
            child._variables[name] = value;
            return child;
        }

        public Scope With(string name, object? value, string? indexName, int index)
        {
            var child = With(name, value);
            if (!string.IsNullOrEmpty(indexName))
            {
                child._variables[indexName!] = (decimal)index;
            }

            return child;
        }

        public bool TryGetVariable(string name, out object? value)
        {
            for (var s = this; s != null; s = s._parent)
            {
                if (s._variables.TryGetValue(name, out value))
                {
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>What a path evaluates to when the data has no such member.</summary>
        public object? Missing(PathExpression path)
        {
            if (Options.Strict)
            {
                throw new TemplateException("The data has no value for '" + path.Text + "'. Fix the name, or turn off Strict mode to render missing values as empty.", path.Line, path.Column);
            }

            return null;
        }
    }
}
