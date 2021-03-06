﻿using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Raven.Client.Embedded;
using Augurk.Api;
using Augurk.Api.Managers;
using System.Threading.Tasks;
using System.Linq;

namespace Augurk.Test.Managers
{
    /// <summary>
    /// Summary description for DependencyManagerTests
    /// </summary>
    [TestClass]
    public class DependencyManagerTests
    {

        [TestInitialize()]
        public void PrepareRavenDB() {
            Database.DocumentStore = new EmbeddableDocumentStore()
            {
                Configuration =
                {
                    RunInUnreliableYetFastModeThatIsNotSuitableForProduction = true,
                    RunInMemory = true,
                }
            };

            Database.DocumentStore.Conventions.IdentityPartsSeparator = "-";
            Database.DocumentStore.Initialize();
        }
     
        /// <summary>
        /// Verifies whether feature graphs can be discovered when two different features are available.
        /// </summary>
        [TestMethod]
        public async Task VerifyFeatureGraphsCanBeDiscovered()
        {
            var callingFeature = CreateDbFeature("CallingFeature", "SomeClass.Foo()");
            var calledFeature = CreateDbFeature("CalledFeature", "SomeOtherClass.Bar()");

            await new FeatureManager().PersistDbFeatures(new[] { callingFeature, calledFeature });

            var invocations = new DbInvocation[]
            {
                new DbInvocation()
                {
                    Signature = "SomeClass.Foo()",
                    InvokedSignatures = new []
                    {
                        "SomeClass.Foo(System.String)",
                        "SomeOtherClass.Bar()"
                    }
                },
                new DbInvocation()
                {
                    Signature = "SomeOtherClass.Bar()",
                    InvokedSignatures = new []
                    {
                        "SomeOtherClass.JuiceBar()"
                    }
                }
            };

            await new AnalysisReportManager().PersistDbInvocationsAsync("TestProduct", "0.0.0", invocations);


            var target = new DependencyManager();

            var graphs = (await target.GetTopLevelFeatureGraphsAsync()).ToList();

            Assert.AreEqual(1, graphs.Count, $"There are {graphs.Count} graphs, instead of just one.");
            Assert.AreEqual("CallingFeature", graphs[0].FeatureName);
            Assert.AreEqual(1, graphs[0].DependsOn.Count);
            Assert.AreEqual("CalledFeature", graphs[0].DependsOn[0].FeatureName);
        }

        /// <summary>
        /// Verifies whether feature graphs can be discovered even when there are features without signatures.
        /// </summary>
        [TestMethod]
        public async Task VerifyFeatureGraphsDiscoveryIsResistantToFeaturesWithoutSignatures()
        {
            var callingFeature = CreateDbFeature("CallingFeature", "SomeClass.Foo()");
            var calledFeature = CreateDbFeature("CalledFeature", "SomeOtherClass.Bar()");
            var unlinkedFeature = CreateDbFeature("UnlinkedFeature");

            await new FeatureManager().PersistDbFeatures(new[] { callingFeature, calledFeature, unlinkedFeature });

            var invocations = new DbInvocation[]
            {
                new DbInvocation()
                {
                    Signature = "SomeClass.Foo()",
                    InvokedSignatures = new []
                    {
                        "SomeClass.Foo(System.String)",
                        "SomeOtherClass.Bar()"
                    }
                },
                new DbInvocation()
                {
                    Signature = "SomeOtherClass.Bar()",
                    InvokedSignatures = new []
                    {
                        "SomeOtherClass.JuiceBar()"
                    }
                }
            };

            await new AnalysisReportManager().PersistDbInvocationsAsync("TestProduct", "0.0.0", invocations);


            var target = new DependencyManager();

            var graphs = (await target.GetTopLevelFeatureGraphsAsync()).ToList();

            Assert.AreEqual(2, graphs.Count, $"There are {graphs.Count} graphs, instead of the two that were expected.");
            Assert.IsTrue(graphs.Any(f => f.FeatureName == "CallingFeature"));
            Assert.IsTrue(graphs.Any(f => f.FeatureName == "UnlinkedFeature"));
        }

        /// <summary>
        /// Verifies whether a lower level feature graph can be retrieved directly.
        /// </summary>
        [TestMethod]
        public async Task VerifyAMidTreeFeatureGraphCanBeRetrieved()
        {
            var callingFeature = CreateDbFeature("CallingFeature", "SomeClass.Foo()");
            var calledFeature = CreateDbFeature("CalledFeature", "SomeOtherClass.Bar()");
            var anotherCalledFeature = CreateDbFeature("AnotherCalledFeature", "SomeOtherClass.JuiceBar()");

            await new FeatureManager().PersistDbFeatures(new[] { callingFeature, calledFeature, anotherCalledFeature });

            var invocations = new DbInvocation[]
            {
                new DbInvocation()
                {
                    Signature = "SomeClass.Foo()",
                    InvokedSignatures = new []
                    {
                        "SomeClass.Foo(System.String)",
                        "SomeOtherClass.Bar()"
                    }
                },
                new DbInvocation()
                {
                    Signature = "SomeOtherClass.Bar()",
                    InvokedSignatures = new []
                    {
                        "SomeOtherClass.JuiceBar()"
                    }
                }
            };

            await new AnalysisReportManager().PersistDbInvocationsAsync("TestProduct", "0.0.0", invocations);


            var target = new DependencyManager();

            var graph = await target.GetFeatureGraphAsync("TestProduct", "CalledFeature", "0.0.0");

            Assert.AreEqual("CalledFeature", graph.FeatureName);
            Assert.AreEqual(1, graph.DependsOn.Count);
            Assert.AreEqual("AnotherCalledFeature", graph.DependsOn[0].FeatureName);
            Assert.AreEqual(1, graph.Dependants.Count);
            Assert.AreEqual("CallingFeature", graph.Dependants[0].FeatureName);
        }

        /// <summary>
        /// Verifies whether a feature graph can be retrieved directly, even when it's code has some recursion going on.
        /// </summary>
        [TestMethod]
        public async Task VerifyAFeatureGraphCanBeRetrievedWhenItsCodeHasRecursion()
        {
            var callingFeature = CreateDbFeature("CallingFeature", "SomeClass.Foo()");
            var calledFeature = CreateDbFeature("CalledFeature", "SomeOtherClass.Bar()");
            var anotherCalledFeature = CreateDbFeature("AnotherCalledFeature", "SomeOtherClass.JuiceBar()");

            await new FeatureManager().PersistDbFeatures(new[] { callingFeature, calledFeature, anotherCalledFeature });

            var invocations = new DbInvocation[]
            {
                new DbInvocation()
                {
                    Signature = "SomeClass.Foo()",
                    InvokedSignatures = new []
                    {
                        "SomeClass.Foo(System.String)",
                        "SomeOtherClass.Bar()"
                    }
                },
                new DbInvocation()
                {
                    Signature = "SomeOtherClass.Bar()",
                    InvokedSignatures = new []
                    {
                        "SomeOtherClass.JuiceBar()"
                    }
                },
                new DbInvocation()
                {
                    Signature = "SomeOtherClass.JuiceBar()",
                    InvokedSignatures = new []
                    {
                        "SomeOtherClass.Bar()"
                    }
                }
            };

            await new AnalysisReportManager().PersistDbInvocationsAsync("TestProduct", "0.0.0", invocations);


            var target = new DependencyManager();

            var graph = await target.GetFeatureGraphAsync("TestProduct", "CalledFeature", "0.0.0");

            Assert.AreEqual("CalledFeature", graph.FeatureName);
            Assert.AreEqual(1, graph.DependsOn.Count);
            Assert.AreEqual("AnotherCalledFeature", graph.DependsOn[0].FeatureName);
            Assert.AreEqual(2, graph.Dependants.Count);
            Assert.AreEqual("CallingFeature", graph.Dependants[0].FeatureName);
            Assert.AreEqual("AnotherCalledFeature", graph.Dependants[1].FeatureName);
        }

        private static DbFeature CreateDbFeature(string featureName, params string[] directInvocationSignatures)
        {
            var result = new DbFeature()
            {
                Product = "TestProduct",
                Group = "TestGroup",
                Title = featureName,
                Version = "0.0.0",
            };

            if (directInvocationSignatures != null && directInvocationSignatures.Length > 0)
            {
                result.DirectInvocationSignatures = new List<string>(directInvocationSignatures);
            }

            return result;
        }
    }
}
