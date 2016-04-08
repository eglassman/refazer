﻿using System;
using System.Linq.Expressions;
using IronPython;
using IronPython.Compiler;
using IronPython.Compiler.Ast;
using IronPython.Hosting;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Hosting.Providers;
using Microsoft.Scripting.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using Community.CsharpSqlite;
using Microsoft.ProgramSynthesis;
using Microsoft.ProgramSynthesis.Compiler;
using Microsoft.ProgramSynthesis.Learning;
using Microsoft.ProgramSynthesis.Specifications;
using Microsoft.ProgramSynthesis.Utils;
using Expression = System.Linq.Expressions.Expression;

namespace Tutor.Tests
{
    [TestClass]
    public class EditsLearnerTest
    {
        [TestMethod]
        public void TestLearn()
        {
            var grammar = DSLCompiler.LoadGrammarFromFile(@"C:\Users\Gustavo\git\Tutor\Tutor\Transformation.grammar");

            var astBefore = NodeWrapper.Wrap(ASTHelper.ParseContent("x = 0"));

            var input = State.Create(grammar.Value.InputSymbol, astBefore);
            var astAfter = NodeWrapper.Wrap(ASTHelper.ParseContent("x = 1"));

            var examples = new Dictionary<State, object> { { input, astAfter } };
            var spec = new ExampleSpec(examples);

            var prose = new SynthesisEngine(grammar.Value);
            var learned = prose.LearnGrammar(spec);
            var first = learned.RealizedPrograms.First();
            var output = first.Invoke(input) as IEnumerable<PythonAst>;
            var fixedProgram = output.First();
            var unparser = new Unparser();
            var newCode = unparser.Unparse(fixedProgram);
            Assert.AreEqual("\r\nx = 1",newCode);

            var secondOutput = first.Invoke(State.Create(grammar.Value.InputSymbol,
                NodeWrapper.Wrap(ASTHelper.ParseContent("1 == 0")))) as IEnumerable<PythonAst>;
            Assert.IsTrue(!secondOutput.AsEnumerable().Any());
        }

        [TestMethod]
        public void TestMatchNode()
        {
            var py = Python.CreateEngine();
            var code = ParseContent("x * a", py);
            var x = new NameExpression("x");
            var a = new NameExpression("a");
            var multiply = new IronPython.Compiler.Ast.BinaryExpression(PythonOperator.Multiply, x, a);
            var root = new PythonNode(multiply, true);
            root.AddChild(new PythonNode(x, true));
            root.AddChild(new PythonNode(a, true));
            var m = new Match(root);
            Assert.IsTrue(m.HasMatch(code));
        }

        [TestMethod]
        public void TestMatchNodeFalse()
        {
            var py = Python.CreateEngine();
            var code = ParseContent("x * 0", py);
            var x = new NameExpression("x");
            var a = new NameExpression("a");
            var multiply = new IronPython.Compiler.Ast.BinaryExpression(PythonOperator.Multiply, x, a);
            var root = new PythonNode(multiply, true);
            root.AddChild(new PythonNode(x, true));
            root.AddChild(new PythonNode(a, true));
            var m = new Match(root);
            Assert.IsFalse(m.HasMatch(code));
        }

        [TestMethod]
        public void TestMatchNode2()
        {
            var py = Python.CreateEngine();
            var code = ParseContent("n == 1", py);
            var n = new NameExpression("n");
            var literal = new IronPython.Compiler.Ast.ConstantExpression(1);
            var multiply = new IronPython.Compiler.Ast.BinaryExpression(PythonOperator.Equals, n, literal);
            var root = new PythonNode(multiply, true);
            root.AddChild(new PythonNode(n, true));
            root.AddChild(new PythonNode(literal, true, 1));
            var m = new Match(root);
            Assert.IsTrue(m.HasMatch(code));
            Assert.AreEqual("literal", m.MatchResult[1].First().NodeName);
        }

        [TestMethod]
        public void TestUpdateNode()
        {
            var py = Python.CreateEngine();
            var code = ParseContent("n == 1", py);
            code.Bind();
            var n = new NameExpression("n");
            var literal = new IronPython.Compiler.Ast.ConstantExpression(1);
            var multiply = new IronPython.Compiler.Ast.BinaryExpression(PythonOperator.Equals, n, literal);
            var root = new PythonNode(multiply, true);
            root.AddChild(new PythonNode(n, true));
            root.AddChild(new PythonNode(literal, true, 1));
            var m = new Match(root);
            Assert.IsTrue(m.HasMatch(code));

            var newNode = new IronPython.Compiler.Ast.ConstantExpression(0);
            var update = new Update(new PythonNode(newNode,false), null);
            var newAst = update.Run(code, m.MatchResult[1].First());
            var ast = newAst as PythonAst;
            var body = ast.Body as SuiteStatement;
            var stmt = body.Statements.First() as ExpressionStatement;
            var binaryExp = stmt.Expression as IronPython.Compiler.Ast.BinaryExpression;
            var constant = binaryExp.Right as IronPython.Compiler.Ast.ConstantExpression;
            Assert.AreEqual(0, constant.Value);
        }

        [TestMethod]
        public void TestUpdateNode2()
        {
            var py = Python.CreateEngine();
            var code = @"
def accumulate(combiner, base, n, term):
    if n == 1:
        return base
    else:
        return combiner(term(n), accumulate(combiner, base, n - 1, term))";
            var ast = ParseContent(code, py);
            ast.Bind();


            var n = new NameExpression("n");
            var literal = new IronPython.Compiler.Ast.ConstantExpression(1);
            var binaryExpression = new IronPython.Compiler.Ast.BinaryExpression(PythonOperator.Equals, n, literal);
            var root = new PythonNode(binaryExpression, true);
            root.AddChild(new PythonNode(n, true));
            root.AddChild(new PythonNode(literal, true, 1));
            var m = new Match(root);

            Assert.IsTrue(m.HasMatch(ast));
            Assert.AreEqual("literal", m.MatchResult[1].First().NodeName);

            var newNode = new IronPython.Compiler.Ast.ConstantExpression(0);
            var update = new Update(new PythonNode(newNode, false), null);
            var newAst = update.Run(ast, m.MatchResult[1].First());

            var expected = @"
def accumulate(combiner, base, n, term):
    if n==0:
        return base
    else:
        return combiner(term(n), accumulate(combiner, base, n-1, term))";
            var actual = new Unparser().Unparse(newAst as PythonAst);

            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void TestRunPythonMethod()
        {
            var py = Python.CreateEngine();
            var code = ParseContent("def identity(n) : \n    return n", py);
            code.Bind();

            var executed = py.Execute(new Unparser().Unparse(code) + "\nidentity(2)");
            Assert.AreEqual(2, executed);
        }

        [TestMethod]
        public void TestUnparser()
        {
            var py = Python.CreateEngine();
            var code = ParseContent("def identity(n) : \n    return n", py);
            code.Bind();
            Assert.AreEqual("\r\ndef identity(n):\r\n    return n", new Unparser().Unparse(code));

        }

        [TestMethod]
        public void TestUnparser2()
        {
            var py = Python.CreateEngine();
            var code = ParseContent("def identity(n) : \n    return n == 0", py);
            code.Bind();
            Assert.AreEqual("\r\ndef identity(n):\r\n    return n==0", new Unparser().Unparse(code));
        }

        [TestMethod]
        public void TestUnparser3()
        {
            var py = Python.CreateEngine();
            var code = @"
def accumulate(combiner, base, n, term):
    if n==1:
        return base
    else:
        return combiner(term(n), accumulate(combiner, base, n-1, term))";
            var ast = ParseContent(code, py);
            ast.Bind();
            var actual = new Unparser().Unparse(ast);
            Assert.AreEqual(code, actual);
        }

//        [TestMethod]
//        public void TestfixProgram()
//        {
//            var program = @"
//def product(n, term):
//    total, k = 0, 1
//    while k <= n:
//        total, k = total * term(k), k + 1
//    return total";

//            var py = Python.CreateEngine();

//            var total = new NameExpression("total");
//            var k = new NameExpression("k");
//            var leftTuple = new TupleExpression(true, total, k);
//            var literal1 = new IronPython.Compiler.Ast.ConstantExpression(0);
//            var literal2 = new IronPython.Compiler.Ast.ConstantExpression(1);
//            var rightTuple = new TupleExpression(true, literal1, literal2);
//            var assign = new AssignmentStatement(new IronPython.Compiler.Ast.Expression[] { leftTuple },
//                rightTuple);
//            var root = new PythonNode(assign, true);
//            var leftNode = new PythonNode(leftTuple, true);
//            leftNode.AddChild(new PythonNode(total, true));
//            leftNode.AddChild(new PythonNode(k, true));
//            root.AddChild(leftNode);
//            var rightNode = new PythonNode(rightTuple, true);
//            rightNode.AddChild(new PythonNode(literal1, true, 1));
//            rightNode.AddChild(new PythonNode(literal2, true));
//            root.AddChild(rightNode);

//            var m = new Match(root);
//            var newNode = new IronPython.Compiler.Ast.ConstantExpression(1);
//            var update = new Update(new PythonNode(newNode, false), null);
//            var fix = new Patch(m, update);

//            var fixer = new SubmissionFixer();

//            var testSetup = @"def square(x):
//    return x * x

//def identity(x):
//    return x
//";
//            var tests = new Dictionary<String, int>
//            {
//                {testSetup + "product(3, identity)", 6},
//                {testSetup + "product(5, identity)", 120},
//                {testSetup + "product(3, square)", 36},
//                {testSetup + "product(5, square)", 14400}
//            };
//            var isFixed = fixer.Fix(program, new List<Patch>() { fix }, tests);
//            Assert.AreEqual(true, isFixed);

//        }

//        [TestMethod]
//        public void TestfixProgram2()
//        {
//            var program = @"
//def product(n, term):
//    z, w = 1, 1
//    total, k = 0, 1
//    while k <= n:
//        total, k = total * term(k), k + 1
//    return total";

//            var py = Python.CreateEngine();

//            var total = new NameExpression("total");
//            var k = new NameExpression("k");
//            var leftTuple = new TupleExpression(true, total, k);
//            var literal1 = new IronPython.Compiler.Ast.ConstantExpression(0);
//            var literal2 = new IronPython.Compiler.Ast.ConstantExpression(1);
//            var rightTuple = new TupleExpression(true, literal1, literal2);
//            var assign = new AssignmentStatement(new IronPython.Compiler.Ast.Expression[] { leftTuple },
//                rightTuple);
//            var root = new PythonNode(assign, true);
//            var leftNode = new PythonNode(leftTuple, true);
//            leftNode.AddChild(new PythonNode(total, true));
//            leftNode.AddChild(new PythonNode(k, true));
//            root.AddChild(leftNode);
//            var rightNode = new PythonNode(rightTuple, true);
//            rightNode.AddChild(new PythonNode(literal1, false, 1));
//            rightNode.AddChild(new PythonNode(literal2, true));
//            root.AddChild(rightNode);

//            var m = new Match(root);
//            var newNode = new IronPython.Compiler.Ast.ConstantExpression(1);
//            var update = new Update(new PythonNode(newNode,false), null);
//            var fix = new Patch(m, update);

//            var fixer = new SubmissionFixer();

//            var testSetup = @"def square(x):
//    return x * x

//def identity(x):
//    return x
//";
//            var tests = new Dictionary<String, int>
//            {
//                {testSetup + "product(3, identity)", 6},
//                {testSetup + "product(5, identity)", 120},
//                {testSetup + "product(3, square)", 36},
//                {testSetup + "product(5, square)", 14400}
//            };
//            var isFixed = fixer.Fix(program, new List<Patch>() { fix }, tests);
//            Assert.AreEqual(true, isFixed);

//        }

//        [TestMethod]
//        public void TestfixProgram3()
//        {
//            var program = @"
//def product(n, term):
//    z, w = 1, 1
//    total, k = 0, 1
//    while k <= n:
//        total, k = total * term(k), k + 1
//    return total";

//            var py = Python.CreateEngine();

//            var total = new NameExpression("total");
//            var k = new NameExpression("k");
//            var leftTuple = new TupleExpression(true, total, k);
//            var literal1 = new IronPython.Compiler.Ast.ConstantExpression(0);
//            var literal2 = new IronPython.Compiler.Ast.ConstantExpression(1);
//            var rightTuple = new TupleExpression(true, literal1, literal2);
//            var assign = new AssignmentStatement(new IronPython.Compiler.Ast.Expression[] { leftTuple },
//                rightTuple);
//            var root = new PythonNode(assign, true);
//            var leftNode = new PythonNode(leftTuple, true);
//            leftNode.AddChild(new PythonNode(total, true));
//            leftNode.AddChild(new PythonNode(k, true));
//            root.AddChild(leftNode);
//            var rightNode = new PythonNode(rightTuple, true);
//            rightNode.AddChild(new PythonNode(literal1, true, 1));
//            rightNode.AddChild(new PythonNode(literal2, true));
//            root.AddChild(rightNode);

//            var m = new Match(root);
//            var newNode = new IronPython.Compiler.Ast.ConstantExpression(1);
//            var update = new Update(new PythonNode(newNode, false), null);
//            var fix = new Patch(m, update);

//            var fixer = new SubmissionFixer();

//            var testSetup = @"def square(x):
//    return x * x

//def identity(x):
//    return x
//";
//            var tests = new Dictionary<String, int>
//            {
//                {testSetup + "product(3, identity)", 6},
//                {testSetup + "product(5, identity)", 120},
//                {testSetup + "product(3, square)", 36},
//                {testSetup + "product(5, square)", 14400}
//            };
//            var isFixed = fixer.Fix(program, new List<Patch>() { fix }, tests);
//            Assert.AreEqual(true, isFixed);

//        }


        private PythonAst ParseContent(string content, ScriptEngine py)
        {
            var src = HostingHelpers.GetSourceUnit(py.CreateScriptSourceFromString(content));
            return Parse(py, src);
        }

        private PythonAst ParseFile(string path, ScriptEngine py)
        {
            var src = HostingHelpers.GetSourceUnit(py.CreateScriptSourceFromFile(path));
            return Parse(py, src);
        }

        private PythonAst Parse(ScriptEngine py, SourceUnit src)
        {
            var pylc = HostingHelpers.GetLanguageContext(py);
            var parser = Parser.CreateParser(new CompilerContext(src, pylc.GetCompilerOptions(), ErrorSink.Default),
                (PythonOptions)pylc.Options);

            return parser.ParseFile(true);
        }
    }
}