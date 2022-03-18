/*
    Copyright 2022 University of Toronto

    This file is part of XTMF2.

    XTMF2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/>.
*/
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using XTMF2.Editing;
using XTMF2.ModelSystemConstruct;
using XTMF2.ModelSystemConstruct.Parameters.Compiler;
using XTMF2.RuntimeModules;

namespace XTMF2.UnitTests.ModelSystemConstruct.Parameters.Compiler;

/// <summary>
/// Provides common functionality for the unit tests for the parameter compiler
/// </summary>
internal static class TestCompilerHelper
{
    /// <summary>
    /// Gives an empty set of nodes for use in the compiler.
    /// </summary>
    internal static readonly List<Node> EmptyNodeList = new();

    /// <summary>
    /// Provides a common call site for testing boolean results.
    /// </summary>
    /// <param name="text">The text value to test.</param>
    /// <param name="expectedResult">The expected result of the text.</param>
    internal static void TestBoolean(string text, bool expectedResult, IList<Node> nodes = null)
    {
        string error = null;
        if (nodes is null)
        {
            nodes = EmptyNodeList;
        }
        Assert.IsTrue(ParameterCompiler.CreateExpression(nodes, text, out var expression, ref error), $"Failed to compile {text}");
        Assert.IsNotNull(expression, "The a null expression was returned!");
        Assert.AreEqual(typeof(bool), expression.Type);
        Assert.IsTrue(ParameterCompiler.Evaluate(null!, expression, out var result, ref error), error);
        if (result is bool boolResult)
        {
            Assert.AreEqual(expectedResult, boolResult);
        }
        else
        {
            Assert.Fail("The result is not an bool!");
        }
    }

    /// <summary>
    /// Provides a common call site for testing boolean results.
    /// </summary>
    /// <param name="text">The text value to test.</param>
    /// <param name="expectedResult">The expected result of the text.</param>
    /// <param name="nodes">The nodes to use as potential variables.</param>
    internal static void TestFails(string text, IList<Node> nodes = null)
    {
        string error = null;
        if(nodes is null)
        {
            nodes = EmptyNodeList;
        }
        Assert.IsFalse(ParameterCompiler.CreateExpression(nodes, text, out var expression, ref error), $"Successfully compiled bad code!: {text}");
        Assert.IsNotNull(error, "There was no error message!");
        Assert.IsNull(expression, "The expression should have been null!");
    }

    /// <summary>
    /// Create a new node to use for variables.
    /// </summary>
    /// <typeparam name="T">The type of the variable to create</typeparam>
    /// <param name="session">The model system session that we are working in.</param>
    /// <param name="user">The user that is creating the nodes.</param>
    /// <param name="name">The name of the node.</param>
    /// <param name="value">The value of the node.</param>
    /// <returns>The resulting node</returns>
    internal static Node CreateNodeForVariable<T>(ModelSystemSession session, User user, string name, string value)
    {
        Assert.IsTrue(session.AddNode(user, session.ModelSystem.GlobalBoundary, name,
            typeof(BasicParameter<T>),
            new Rectangle(0, 0, 0, 0), out var node, out var error), error?.Message);
        Assert.IsTrue(session.SetParameterValue(user, node, value, out error), error?.Message);
        return node;
    }
}
