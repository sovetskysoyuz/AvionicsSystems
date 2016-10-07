﻿#define PLENTIFUL_LOGGING
#if PLENTIFUL_LOGGING
//#define EXCESSIVE_LOGGING
#endif
/*****************************************************************************
 * The MIT License (MIT)
 * 
 * Copyright (c) 2016 MOARdV
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * 
 ****************************************************************************/
using MoonSharp.Interpreter;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace AvionicsSystems
{
    public partial class MASFlightComputer : PartModule
    {
        /// <summary>
        /// Dictionary mapping variable names to Variable objects.
        /// </summary>
        private Dictionary<string, Variable> variables = new Dictionary<string, Variable>();

        /// <summary>
        /// Converts unformatted strings to canonical formats.
        /// </summary>
        private Dictionary<string, string> canonicalVariableName = new Dictionary<string, string>();

        /// <summary>
        /// List of variables that change (as opposed to constants).
        /// </summary>
        private List<Variable> mutableVariablesList = new List<Variable>();

        /// <summary>
        /// Because a List is more expensive to traverse than an array, and
        /// because we are going to iterate over the mutable variables
        /// *A*LOT*, we keep an array that we really iterate over.
        /// </summary>
        private Variable[] mutableVariables = new Variable[0];

        private int luaVariableCount = 0;
        private int nativeVariableCount = 0;
        private int constantVariableCount = 0;

        static private Dictionary<string, Type> typeMap = new Dictionary<string, Type>
        {
            {"fc", typeof(MASFlightComputerProxy)},
            {"chatterer", typeof(MASIChatterer)},
            {"far", typeof(MASIFAR)},
            {"kac", typeof(MASIKAC)},
            {"mechjeb", typeof(MASIMechJeb)},
            {"nav", typeof(MASINavigation)},
            {"realchute", typeof(MASIRealChute)},
        };

        /// <summary>
        /// Given a table (object) name, a method name, and an array of
        /// parameters, use reflection to find the object and Method that
        /// corresponds to that table.method pair.
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="methodName"></param>
        /// <param name="parameters"></param>
        /// <param name="tableRef"></param>
        /// <param name="methodInfo"></param>
        /// <returns></returns>
        private bool GetMASIMethod(string tableName, string methodName, Type[] parameters, out object tableRef, out MethodInfo methodInfo)
        {
            methodInfo = null;
            tableRef = null;
            //if (typeMap.ContainsKey(tableName)) // Did this already by the caller
            {
                methodInfo = FindMethod(methodName, typeMap[tableName], parameters);

                if (methodInfo != null)
                {
                    bool parameterMatch = false;
                    ParameterInfo[] parms = methodInfo.GetParameters();
                    if (parms.Length == parameters.Length)
                    {
                        parameterMatch = true;
                        for (int i = parms.Length - 1; i >= 0; --i)
                        {
                            if (!(parms[i].ParameterType == typeof(object) || parameters[i] == typeof(object) || parms[i].ParameterType == parameters[i]))
                            {
                                parameterMatch = false;
                                break;
                            }
                        }
                    }

                    if (parameterMatch)
                    {
                        switch (tableName)
                        {
                            case "fc":
                                tableRef = fcProxy;
                                break;
                            case "chatterer":
                                tableRef = chattererProxy;
                                break;
                            case "far":
                                tableRef = farProxy;
                                break;
                            case "kac":
                                tableRef = kacProxy;
                                break;
                            case "mechjeb":
                                tableRef = mjProxy;
                                break;
                            case "nav":
                                tableRef = navProxy;
                                break;
                            case "realchute":
                                tableRef = realChuteProxy;
                                break;
                        }

                        return (tableRef != null);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Find a method with the supplied name within the supplied class (Type)
        /// with a parameter list that matches the types in the VariableParameter array.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="type"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        //private static MethodInfo FindMethod(string name, Type type, VariableParameter[] parameters)
        //{
        //    int numParams = parameters.Length;
        //    MethodInfo[] methods = type.GetMethods();
        //    for (int i = methods.Length - 1; i >= 0; --i)
        //    {
        //        if (methods[i].Name == name)
        //        {
        //            ParameterInfo[] methodParams = methods[i].GetParameters();
        //            if (methodParams.Length == numParams)
        //            {
        //                if (numParams == 0)
        //                {
        //                    return methods[i];
        //                }
        //                else
        //                {
        //                    bool match = true;
        //                    for (int index = 0; index < numParams; ++index)
        //                    {
        //                        if (methodParams[index].ParameterType != parameters[index].valueType)
        //                        {
        //                            match = false;
        //                            break;
        //                        }
        //                    }

        //                    if (match)
        //                    {
        //                        return methods[i];
        //                    }
        //                }
        //            }
        //        }
        //    }

        //    return null;
        //}

        private static MethodInfo FindMethod(string name, Type type, Type[] parameters)
        {
            int numParams = parameters.Length;
            MethodInfo[] methods = type.GetMethods();
            for (int i = methods.Length - 1; i >= 0; --i)
            {
                if (methods[i].Name == name)
                {
                    ParameterInfo[] methodParams = methods[i].GetParameters();
                    if (methodParams.Length == numParams)
                    {
                        if (numParams == 0)
                        {
                            return methods[i];
                        }
                        else
                        {
                            bool match = true;
                            for (int index = 0; index < numParams; ++index)
                            {
                                //if (methodParams[index].ParameterType != parameters[index])
                                if (!(methodParams[index].ParameterType == typeof(object) || parameters[index] == typeof(object) || methodParams[index].ParameterType == parameters[index]))
                                {
                                    match = false;
                                    break;
                                }
                            }

                            if (match)
                            {
                                return methods[i];
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// See if we can parse the tokens sufficiently to create a delegate
        /// that allows us to bypass the MoonSharp interpreter.  Returns
        /// null on failure.
        /// </summary>
        /// <param name="tokens"></param>
        /// <param name="lastToken"></param>
        /// <returns></returns>
        //        private Variable TryCreateNativeVariable(string variableName, Token[] tokens, int lastToken)
        //        {
        //            Variable newVar = null;
        //            if (NativeMethod(tokens, 0, lastToken))
        //            {
        //#if VERBOSE_PARSING
        //                Utility.LogMessage(this, "Native candidate...");
        //#endif
        //                // This looks promising...  Can we figure out how to construct a variable?
        //                VariableParameter[] parameters = TryParseParameters(tokens, 4, lastToken - 1);

        //                if (parameters.Length == 1 && parameters[0].vpType == VariableParameterType.UNSUPPORTED)
        //                {
        //#if VERBOSE_PARSING
        //                    Utility.LogMessage(this, "... Unsupported component in parameter list");
        //#endif
        //                    return null;
        //                }
        //                else
        //                {
        //                    MethodInfo method = FindMethod(tokens[2].Text, typeMap[tokens[0].Text], parameters);
        //                    if (method == null)
        //                    {
        //                        Utility.LogErrorMessage(this, "... Did not find matching method for '{0}'", tokens[2].Text);
        //                    }
        //                    else
        //                    {
        //#if VERBOSE_PARSING
        //                        Utility.LogMessage(this, "... Found matching method for '{0}'", tokens[2].Text);
        //#endif
        //                        object objRef = null;
        //                        switch (tokens[0].Text)
        //                        {
        //                            case "fc":
        //                                objRef = fcProxy;
        //                                break;
        //                            case "far":
        //                                objRef = farProxy;
        //                                break;
        //                            case "chatterer":
        //                                objRef = chattererProxy;
        //                                break;
        //                            case "mechjeb":
        //                                objRef = mjProxy;
        //                                break;
        //                            case "realchute":
        //                                objRef = realChuteProxy;
        //                                break;
        //                        }
        //                        if (objRef != null)
        //                        {
        //                            if (parameters.Length == 0)
        //                            {
        //                                // No parameters.
        //                                DynamicMethod<object> dm = DynamicMethodFactory.CreateFunc<object>(method);
        //                                if (dm != null)
        //                                {
        //                                    newVar = new Variable(variableName, () => { return dm(objRef); });
        //#if VERBOSE_PARSING
        //                                    Utility.LogMessage(this, "Added native variable \"{0}\"", variableName);
        //#endif
        //                                }
        //                            }
        //                            else
        //                            {
        //                                if (parameters.Length == 1)
        //                                {
        //                                    if (parameters[0].vpType == VariableParameterType.CONST_BOOL)
        //                                    {
        //                                        DynamicMethod<object, bool> dm = DynamicMethodFactory.CreateFunc<object, bool>(method);
        //                                        bool bValue = parameters[0].booleanValue;
        //                                        newVar = new Variable(variableName, () => { return dm(objRef, bValue); });
        //#if VERBOSE_PARSING
        //                                        Utility.LogMessage(this, "Added native bool variable \"{0}\"", variableName);
        //#endif
        //                                    }
        //                                    else if (parameters[0].vpType == VariableParameterType.CONST_DOUBLE)
        //                                    {
        //                                        DynamicMethod<object, double> dm = DynamicMethodFactory.CreateFunc<object, double>(method);
        //                                        double dValue = parameters[0].numericValue;
        //                                        newVar = new Variable(variableName, () => { return dm(objRef, dValue); });
        //#if VERBOSE_PARSING
        //                                        Utility.LogMessage(this, "Added native double variable \"{0}\"", variableName);
        //#endif
        //                                    }
        //                                    else if (parameters[0].vpType == VariableParameterType.CONST_STRING)
        //                                    {
        //                                        DynamicMethod<object, string> dm = DynamicMethodFactory.CreateFunc<object, string>(method);
        //                                        string sValue = parameters[0].stringValue;
        //                                        newVar = new Variable(variableName, () => { return dm(objRef, sValue); });
        //#if VERBOSE_PARSING
        //                                        Utility.LogMessage(this, "Added native string variable \"{0}\"", variableName);
        //#endif
        //                                    }
        //                                    else if (parameters[0].vpType == VariableParameterType.VARIABLE)
        //                                    {
        //                                        if (parameters[0].valueType == typeof(bool))
        //                                        {
        //                                            DynamicMethod<object, bool> dm = DynamicMethodFactory.CreateFunc<object, bool>(method);
        //                                            Variable v = parameters[0].variableValue;
        //                                            newVar = new Variable(variableName, () =>
        //                                            {
        //                                                bool bValue = (bool)v.RawValue();
        //                                                return dm(objRef, bValue);
        //                                            });
        //#if VERBOSE_PARSING
        //                                            Utility.LogMessage(this, "Added variable bool variable \"{0}\"", variableName);
        //#endif
        //                                        }
        //                                        else if (parameters[0].valueType == typeof(double))
        //                                        {
        //                                            DynamicMethod<object, double> dm = DynamicMethodFactory.CreateFunc<object, double>(method);
        //                                            Variable v = parameters[0].variableValue;
        //                                            newVar = new Variable(variableName, () =>
        //                                            {
        //                                                return dm(objRef, v.SafeValue());
        //                                            });
        //#if VERBOSE_PARSING
        //                                            Utility.LogMessage(this, "Added variable double variable \"{0}\"", variableName);
        //#endif
        //                                        }
        //                                        else if (parameters[0].valueType == typeof(string))
        //                                        {
        //                                            DynamicMethod<object, string> dm = DynamicMethodFactory.CreateFunc<object, string>(method);
        //                                            Variable v = parameters[0].variableValue;
        //                                            newVar = new Variable(variableName, () =>
        //                                            {
        //                                                return dm(objRef, v.String());
        //                                            });
        //#if VERBOSE_PARSING
        //                                            Utility.LogMessage(this, "Added variable string variable \"{0}\"", variableName);
        //#endif
        //                                        }
        //                                    }
        //                                    else
        //                                    {
        //                                        Utility.LogErrorMessage(this, "Found unsupported parameter type {0} for variable {1}", parameters[0].vpType, variableName);
        //                                    }
        //                                }
        //                                else
        //                                {
        //                                    Utility.LogErrorMessage(this, "Found unsupported {0} parameter method for {1}", parameters.Length, variableName);
        //                                }
        //                            }
        //                        }
        //                    }
        //                }
        //            }

        //            return newVar;
        //        }

        /// <summary>
        /// Get the named Variable (for direct access).
        /// 
        /// So, that's the really short summary, but it does not tell you what
        /// I am really doing.
        /// 
        /// The MoonSharp interpreter, version 1.6.0, seems to be able to
        /// process about 90-ish variables per millisecond using the test IVA
        /// I created (Yarbrough Mk1-1A2, 228 mutable variables).  That's really
        /// poor performance - around 2.5ms per FixedUpdate, which fires every
        /// 20ms at default frequency.  In other words, about 1/8 of the FixedUpdate
        /// time budget, just for the MAS variables.  No bueno.
        /// 
        /// So, I use a Lexer object (see Parser/Lexer.cs) and the Bantam Pratt
        /// parser to transform the variable to an expression tree.  The expression
        /// tree allows me to break the variable into easy-to-digest components,
        /// each of which is shoved into a delegate (or directly into a Variable
        /// for constant values).  Doing so greatly expands the number of Variable
        /// objects I create, but it also allows me to pull more processing away from
        /// the slow Lua interpreter and into quicker delegates.
        /// 
        /// A simple initial implementation where I transfered constants and simple
        /// single-parameter methods (eg 'fc.GetPersistentAsNumber("SomePersistent"),
        /// moves the refresh rate above 120 updates/ms.  That's better.  The fewer
        /// calls into Lua / MoonSharp, the better.  The simple implementation also
        /// accounted for about half of the total number of variables.
        /// </summary>
        /// <param name="variableName"></param>
        /// <param name="prop"></param>
        /// <returns></returns>
        internal Variable GetVariable(string variableName, InternalProp prop)
        {
            variableName = ConditionVariableName(variableName, prop);
            if (variableName.Length < 1)
            {
                Utility.ComplainLoudly("GetVariable with empty variableName");
                throw new ArgumentException("[MASFlightComputer] Trying to GetVariable with empty variableName");
            }

            CodeGen.Parser.CompilerResult result = CodeGen.Parser.TryParse(variableName);
            Variable v = null;
            if (variables.ContainsKey(result.canonicalName))
            {
                v = variables[result.canonicalName];
            }
            else
            {
#if PLENTIFUL_LOGGING
                Utility.LogMessage(this, "*  *  *");
                Utility.LogMessage(this, "Generating variable from {0}", result.canonicalName);
#endif
                if (result.type == CodeGen.Parser.ResultType.NUMERIC_CONSTANT)
                {
#if EXCESSIVE_LOGGING
                    Utility.LogMessage(this, "- NUMERIC_CONSTANT");
#endif
                    v = new Variable(result.numericConstant);
                    ++constantVariableCount;
                }
                else if (result.type == CodeGen.Parser.ResultType.STRING_CONSTANT)
                {
#if EXCESSIVE_LOGGING
                    Utility.LogMessage(this, "- STRING_CONSTANT");
#endif
                    v = new Variable(result.stringConstant);
                    ++constantVariableCount;
                }
                else if (result.type == CodeGen.Parser.ResultType.EXPRESSION_TREE)
                {
#if PLENTIFUL_LOGGING
                    Utility.LogMessage(this, "- EXPRESSION_TREE");
#endif
                    v = GenerateVariable(result.expressionTree);
                    if (v != null && v.variableType == Variable.VariableType.Func)
                    {
                        ++nativeVariableCount;
                    }
                }

                if (v == null)
                {
#if PLENTIFUL_LOGGING
                    Utility.LogMessage(this, "- fall back to Lua scripting");
#endif
                    // If we couldn't find a way to optimize the value, fall
                    // back to interpreted Lua script.
                    v = new Variable(result.canonicalName, script);
                    ++luaVariableCount;
                }
                if (!variables.ContainsKey(result.canonicalName))
                {
                    variables.Add(result.canonicalName, v);
                    if (v.mutable)
                    {
                        mutableVariablesList.Add(v);
                        mutableVariablesChanged = true;
                    }
                    Utility.LogMessage(this, "Adding new variable '{0}'", result.canonicalName);
                }
            }

            return v;
        }

        /// <summary>
        /// Transform an expression into a delegate.
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        private Variable GenerateVariable(CodeGen.Expression expression)
        {
            string canonical = expression.CanonicalName();
            if (variables.ContainsKey(canonical))
            {
                return variables[canonical];
            }

            Variable v = null;
            switch (expression.ExpressionType())
            {
                case CodeGen.ExpressionIs.ConstantNumber:
#if EXCESSIVE_LOGGING
                    Utility.LogMessage(this, "-- GenerateVariable(): NumberExpression");
#endif
                    v = new Variable((expression as CodeGen.NumberExpression).getNumber());
                    break;
                case CodeGen.ExpressionIs.ConstantString:
#if EXCESSIVE_LOGGING
                    Utility.LogMessage(this, "-- GenerateVariable(): StringExpression");
#endif
                    v = new Variable((expression as CodeGen.StringExpression).getString());
                    break;
                case CodeGen.ExpressionIs.Operator:
#if EXCESSIVE_LOGGING
                    Utility.LogMessage(this, "-- GenerateVariable(): OperatorExpression");
#endif
                    v = GenerateOperatorVariable(expression as CodeGen.OperatorExpression);
                    break;
                case CodeGen.ExpressionIs.PrefixOperator:
#if EXCESSIVE_LOGGING
                    Utility.LogMessage(this, "-- GenerateVariable(): PrefixExpression");
#endif
                    v = GeneratePrefixVariable(expression as CodeGen.PrefixExpression);
                    break;
                case CodeGen.ExpressionIs.Call:
#if EXCESSIVE_LOGGING
                    Utility.LogMessage(this, "-- GenerateVariable(): CallExpression");
#endif
                    v = GenerateCallVariable(expression as CodeGen.CallExpression);
                    break;
                default:
                    Utility.LogErrorMessage(this, "!! GenerateVariable(): Unhandled expression type {0}", expression.GetType());
                    //v = new Variable(canonical, script);
                    break;
            }

            if (v != null && !variables.ContainsKey(canonical))
            {
                variables.Add(canonical, v);
                if (v.mutable)
                {
                    mutableVariablesList.Add(v);
                    mutableVariablesChanged = true;
                }

                if (v.variableType == Variable.VariableType.Constant)
                {
                    ++constantVariableCount;
                }
                else if (v.variableType == Variable.VariableType.Func)
                {
                    ++nativeVariableCount;
                }
                else if (v.variableType == Variable.VariableType.LuaScript)
                {
                    ++luaVariableCount;
                }
            }

            return v;
        }

        /// <summary>
        /// Take a binary operator (+, -, *, /, <, >, etc) and transform it into
        /// a variable.
        /// </summary>
        /// <param name="operatorExpression"></param>
        /// <returns></returns>
        private Variable GenerateOperatorVariable(CodeGen.OperatorExpression operatorExpression)
        {
            Utility.LogMessage(this, "--- GenerateOperatorVariable(): operator {0}", operatorExpression.Operator());
            return null;
        }

        /// <summary>
        /// Transform a prefix expression of the form (-number) into a numeric
        /// constant.
        /// </summary>
        /// <param name="prefixExpression"></param>
        /// <returns></returns>
        private Variable GeneratePrefixVariable(CodeGen.PrefixExpression prefixExpression)
        {
            CodeGen.Expression right = prefixExpression.getRight();

            if (prefixExpression.getOperator() == CodeGen.Parser.LuaToken.MINUS && right is CodeGen.NumberExpression)
            {
                double numericConstant = -(right as CodeGen.NumberExpression).getNumber();
                return new Variable(numericConstant);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Given a CallExpression, convert it into either a native delegate or a Lua
        /// script function.
        /// </summary>
        /// <param name="callExpression"></param>
        /// <returns></returns>
        private Variable GenerateCallVariable(CodeGen.CallExpression callExpression)
        {
            string canonical = callExpression.CanonicalName();

            if (callExpression.Function().ExpressionType() == CodeGen.ExpressionIs.DotOperator)
            {
                int numArgs = callExpression.NumArgs();
                Variable[] parms = new Variable[numArgs];
                Type[] parameters = new Type[numArgs];
#if EXCESSIVE_LOGGING
                Utility.LogMessage(this, "--- GenerateCallVariable(): {0} parameters", numArgs);
#endif
                for (int i = 0; i < numArgs; ++i)
                {
                    CodeGen.Expression exp = callExpression.Arg(i);
#if EXCESSIVE_LOGGING
                    Utility.LogMessage(this, "--- GenerateCallVariable(): Parameter {0} is {1} (a {2})", i, sb.ToString(), exp.ExpressionType());
#endif
                    parms[i] = GenerateVariable(exp);
                    if (parms[i] == null)
                    {
                        Utility.LogErrorMessage(this, "!!! GenerateCallVariable(): Unable to generate variable for parameter {0}, punting", i, exp.CanonicalName());
                        return null;
                    }
                    else
                    {
                        parameters[i] = parms[i].RawValue().GetType();
#if EXCESSIVE_LOGGING
                        Utility.LogMessage(this, "--- GenerateCallVariable(): parameter[{0}] is {1}", i, parameters[i]);
#endif
                    }
                }

                object tableInstance;
                MethodInfo method;
                EvaluateDotOperator(callExpression.Function() as CodeGen.DotOperatorExpression, parameters, out tableInstance, out method);

                if (tableInstance != null)
                {
                    ParameterInfo[] methodParams = method.GetParameters();
                    if (numArgs == 0)
                    {
                        DynamicMethod<object> dm = DynamicMethodFactory.CreateFunc<object>(method);
#if EXCESSIVE_LOGGING
                        Utility.LogMessage(this, "--- GenerateCallVariable(): Creating variable for {0}, {1} parameters", canonical, numArgs);
#endif
                        return new Variable(canonical, () => dm(tableInstance));
                    }
                    else if (numArgs == 1)
                    {
                        if (methodParams[0].ParameterType == typeof(double))
                        {
#if EXCESSIVE_LOGGING
                            Utility.LogMessage(this, "--- GenerateCallVariable(): Creating variable for {0}, with 1 parameter of type {1}", canonical, methodParams[0].ParameterType);
#endif
                            DynamicMethod<object, double> dm = DynamicMethodFactory.CreateFunc<object, double>(method);
                            return new Variable(canonical, () => dm(tableInstance, parms[0].SafeValue()));
                        }
                        else if (methodParams[0].ParameterType == typeof(string))
                        {
#if EXCESSIVE_LOGGING
                            Utility.LogMessage(this, "--- GenerateCallVariable(): Creating variable for {0}, with 1 parameter of type {1}", canonical, methodParams[0].ParameterType);
#endif
                            DynamicMethod<object, string> dm = DynamicMethodFactory.CreateFunc<object, string>(method);
                            return new Variable(canonical, () => dm(tableInstance, parms[0].String()));
                        }
                        else if (methodParams[0].ParameterType == typeof(object))
                        {
#if EXCESSIVE_LOGGING
                            Utility.LogMessage(this, "--- GenerateCallVariable(): Creating variable for {0}, with 1 parameter of type {1}", canonical, methodParams[0].ParameterType);
#endif
                            DynamicMethod<object, object> dm = DynamicMethodFactory.CreateFunc<object, object>(method);
                            return new Variable(canonical, () => dm(tableInstance, parms[0].RawValue()));
                        }
                        else
                        {
                            Utility.LogErrorMessage(this, "!!! GenerateCallVariable(): Don't know how to create variable for {0}, with 1 parameter of type {1}", canonical, methodParams[0].ParameterType);
                        }
                    }
                }
#if PLENTIFUL_LOGGING
                else
                {
                    Utility.LogMessage(this, "!!! GenerateCallVariable(): Did not find method for {0}", canonical);
                }
#endif
            }
#if EXCESSIVE_LOGGING
            else
            {
                Utility.LogMessage(this, "--- GenerateCallVariable(): Not able to find method for {0}", canonical);
            }
#endif

            return null;
        }

        /// <summary>
        /// Dot Operator evaluation for the case of a table.method pair - does not handle
        /// convention Lua tables.  It's only for the case of a reflected object.method,
        /// such as fc.Pitch().
        /// </summary>
        /// <param name="dotOperatorExpression"></param>
        /// <param name="parameters"></param>
        /// <param name="tableInstance"></param>
        /// <param name="method"></param>
        private void EvaluateDotOperator(CodeGen.DotOperatorExpression dotOperatorExpression, Type[] parameters, out object tableInstance, out MethodInfo method)
        {
            tableInstance = null;
            method = null;

            StringBuilder sb = Utility.GetStringBuilder();
            dotOperatorExpression.print(sb);

            CodeGen.NameExpression tableName = dotOperatorExpression.TableName() as CodeGen.NameExpression;
            CodeGen.NameExpression methodName = dotOperatorExpression.MethodName() as CodeGen.NameExpression;

            if (tableName != null && methodName != null && typeMap.ContainsKey(tableName.getName()))
            {
                GetMASIMethod(tableName.getName(), methodName.getName(), parameters, out tableInstance, out method);
            }
        }

        /// <summary>
        /// The Variable is a wrapper class to manage a single variable (as
        /// defined by a Lua script or a constant value).  It allows the MAS
        /// Flight Computer to track and update a single instance of a given
        /// variable, so heavily-used variables do not cause excessive
        /// performance penalties by being queried dozens or hundreds of
        /// times.
        /// </summary>
        public class Variable
        {
            public readonly string name;
            public bool mutable
            {
                get
                {
                    return variableType != VariableType.Constant;
                }
            }
            public readonly bool valid;
            internal event Action<double> numericCallbacks;
            internal event Action changeCallbacks;
            private Func<object> nativeEvaluator;
            private DynValue luaEvaluator;
            private object rawObject;
            private string stringValue;
            private double doubleValue;
            private double safeValue;
            internal readonly VariableType variableType = VariableType.Unknown;

            /// <summary>
            /// How do we evaluate this variable?
            /// </summary>
            public enum VariableType
            {
                Unknown,
                LuaScript,
                Constant,
                Func,
            };

            /// <summary>
            /// Construct a constant numeric Variable.
            /// </summary>
            /// <param name="value"></param>
            public Variable(double value)
            {
                this.name = string.Format("{0:R}", value);

                this.valid = true;
                this.stringValue = this.name;
                this.doubleValue = value;
                this.safeValue = value;
                this.rawObject = value;
                this.variableType = VariableType.Constant;
            }

            /// <summary>
            /// Construct a constant string Variable.
            /// </summary>
            /// <param name="value"></param>
            public Variable(string value)
            {
                this.name = value;

                this.valid = true;
                this.stringValue = value;
                this.doubleValue = double.NaN;
                this.safeValue = 0.0;
                this.rawObject = value;
                this.variableType = VariableType.Constant;
            }

            /// <summary>
            /// Construct a dynamic native evaluator.
            /// </summary>
            /// <param name="name"></param>
            /// <param name="nativeEvaluator"></param>
            /// <param name="mutable"></param>
            public Variable(string name, Func<object> nativeEvaluator)
            {
                this.name = name;

                this.nativeEvaluator = nativeEvaluator;
                object value = nativeEvaluator();
                this.variableType = VariableType.Func;

                ProcessObject(value);

                this.valid = true;
            }

            /// <summary>
            /// Construct a dynamic Lua Variable.
            /// </summary>
            /// <param name="name"></param>
            /// <param name="script"></param>
            public Variable(string name, Script script)
            {
                this.name = name;

                DynValue luaValue = null;
                try
                {
                    // TODO: MoonSharp "hardwiring" - does it help performance?
                    luaEvaluator = script.LoadString("return " + name);
                    luaValue = script.Call(luaEvaluator);
                    this.valid = true;
                }
                catch (Exception e)
                {
                    Utility.ComplainLoudly("Error creating variable " + name);
                    Utility.LogErrorMessage(this, "Unknown variable '{0}':", name);
                    Utility.LogErrorMessage(this, e.ToString());
                    luaValue = null;
                    this.valid = false;
                }

                if (this.valid)
                {
                    this.variableType = VariableType.LuaScript;
                    ProcessObject(luaValue.ToObject());
                }
            }

            /// <summary>
            /// Return the raw object for customized processing.
            /// </summary>
            /// <returns></returns>
            public object RawValue()
            {
                return rawObject;
            }

            /// <summary>
            /// Return the raw double value of this variable (including NaN and Inf).
            /// </summary>
            /// <returns></returns>
            public double Value()
            {
                return doubleValue;
            }

            /// <summary>
            /// Return the safe value of this variable (NaN and Inf are silently treated
            /// as 0.0).
            /// </summary>
            /// <returns></returns>
            public double SafeValue()
            {
                return safeValue;
            }

            /// <summary>
            /// Return the value as a string.
            /// </summary>
            /// <returns></returns>
            public string String()
            {
                return stringValue;
            }

            /// <summary>
            /// Process and classify the raw object that comes from the native
            /// evaluator.
            /// </summary>
            /// <param name="value"></param>
            private void ProcessObject(object value)
            {
                if (rawObject == null || !value.Equals(rawObject))
                {
                    double oldSafeValue = safeValue;
                    rawObject = value;

                    if (value is double)
                    {
                        doubleValue = (double)value;
                        safeValue = doubleValue;
                        stringValue = doubleValue.ToString();
                    }
                    else if (value is string)
                    {
                        stringValue = value as string;
                        doubleValue = double.NaN;
                        safeValue = 0.0;
                    }
                    else if (value is bool)
                    {
                        bool bValue = (bool)value;
                        safeValue = (bValue) ? 1.0 : 0.0;
                        doubleValue = double.NaN;
                        stringValue = bValue.ToString();
                    }
                    else if (value is MASVector2)
                    {
                        Vector2 v = (Vector2)(MASVector2)value;
                        safeValue = v.sqrMagnitude;
                        doubleValue = safeValue;
                        stringValue = v.ToString();
                    }
                    else if (value == null)
                    {
                        safeValue = 0.0;
                        doubleValue = double.NaN;
                        stringValue = name;
                    }
                    else
                    {
                        // TODO ...?
                        throw new NotImplementedException("ProcessObject found an unexpected return type " + value.GetType() + " for " + name);
                    }

                    if (!Mathf.Approximately((float)safeValue, (float)oldSafeValue))
                    {
                        if (numericCallbacks != null)
                        {
                            numericCallbacks.Invoke(safeValue);
                        }
                    }

                    if (changeCallbacks != null)
                    {
                        changeCallbacks.Invoke();
                    }
                }
            }

            /// <summary>
            /// Evaluate updates the variable in question by calling the code
            /// snippet using the supplied Lua script.
            /// </summary>
            /// <param name="script"></param>
            internal void Evaluate(Script script)
            {
                if (variableType == VariableType.LuaScript)
                {
                    DynValue value;
                    try
                    {
                        value = script.Call(luaEvaluator);
                    }
                    catch
                    {
                        value = DynValue.NewNil();
                    }

                    ProcessObject(value.ToObject());
                }
                else if (variableType == VariableType.Func)
                {
                    object value = nativeEvaluator();

                    ProcessObject(value);
                }
            }
        }
    }
}
