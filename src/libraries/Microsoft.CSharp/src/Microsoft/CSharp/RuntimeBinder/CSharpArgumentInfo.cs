// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;

namespace Microsoft.CSharp.RuntimeBinder
{
    /// <summary>
    /// Represents information about C# dynamic operations that are specific to particular arguments at a call site.
    /// Instances of this class are generated by the C# compiler.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class CSharpArgumentInfo
    {
        // Create a singleton static instance.
        internal static readonly CSharpArgumentInfo None = new CSharpArgumentInfo(CSharpArgumentInfoFlags.None, null);

        /// <summary>
        /// The flags for the argument.
        /// </summary>
        private CSharpArgumentInfoFlags Flags { get; }

        /// <summary>
        /// The name of the argument, if named; otherwise null.
        /// </summary>
        internal string Name { get; }

        private CSharpArgumentInfo(CSharpArgumentInfoFlags flags, string name)
        {
            Flags = flags;
            Name = name;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CSharpArgumentInfo"/> class.
        /// </summary>
        /// <param name="flags">The flags for the argument.</param>
        /// <param name="name">The name of the argument, if named; otherwise null.</param>
        public static CSharpArgumentInfo Create(CSharpArgumentInfoFlags flags, string name)
        {
            return new CSharpArgumentInfo(flags, name);
        }

        // Accessor helpers.
        internal bool UseCompileTimeType => (Flags & CSharpArgumentInfoFlags.UseCompileTimeType) != 0;

        internal bool LiteralConstant => (Flags & CSharpArgumentInfoFlags.Constant) != 0;

        internal bool NamedArgument => (Flags & CSharpArgumentInfoFlags.NamedArgument) != 0;

        internal bool IsByRefOrOut => (Flags & (CSharpArgumentInfoFlags.IsRef | CSharpArgumentInfoFlags.IsOut)) != 0;

        internal bool IsOut => (Flags & CSharpArgumentInfoFlags.IsOut) != 0;

        internal bool IsStaticType => (Flags & CSharpArgumentInfoFlags.IsStaticType) != 0;
    }
}
