using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Packaging.Tests
{
    [TestClass]
    public sealed class WorkflowPublishingConditionTests
    {
        public static IEnumerable<object[]> PublishingCases()
        {
            foreach (var eventName in new[] { "push", "pull_request", "workflow_dispatch" })
            foreach (var reference in new[]
            {
                "refs/tags/1.2.3", "refs/tags/1.2.3-beta.1", "refs/heads/main",
                "refs/heads/release/1.2", "refs/heads/hotfix/1.2.3", "refs/heads/develop",
                "refs/heads/feature/test", "refs/heads/release-candidate"
            })
            foreach (var publish in new[] { false, true })
            foreach (var dryRun in new[] { false, true })
                yield return new object[] { eventName, reference, publish, dryRun };
        }

        [TestMethod]
        [DynamicData(nameof(PublishingCases))]
        public void PublishingRequiresAnAuthorizedEventAndSuccessfulArtifactHandoff(
            string eventName, string reference, bool publish, bool dryRun)
        {
            var workflow = Workflow.Read();
            var context = Context(eventName, reference, publish, dryRun);
            var tagPush = eventName == "push" && reference.StartsWith("refs/tags/", StringComparison.Ordinal);
            var manualPublish = eventName == "workflow_dispatch" && publish && !dryRun &&
                (reference == "refs/heads/main" || reference.StartsWith("refs/heads/release/", StringComparison.Ordinal) ||
                 reference.StartsWith("refs/heads/hotfix/", StringComparison.Ordinal));
            var expectedPublish = tagPush || manualPublish;
            var expectedPack = tagPush || manualPublish || (eventName == "workflow_dispatch" && dryRun);

            // Check both the source condition and the source needs graph. A skipped pack can
            // conceal an unsafe publish expression, as it did for non-dry-run tag dispatches.
            Assert.AreEqual(expectedPublish, workflow.Condition("publish", context), "Raw publish condition.");
            var results = workflow.Schedule(context);
            Assert.AreEqual(expectedPack ? "success" : "skipped", results["pack-and-validate"]);
            Assert.AreEqual(expectedPack ? "success" : "skipped", results["verify-release-artifacts"]);
            Assert.AreEqual(expectedPublish ? "success" : "skipped", results["publish"]);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void TagSelectedRehearsalVerifiesArtifactsWithoutCallingNuGetPush(bool publish)
        {
            var workflow = Workflow.Read();
            var results = workflow.Schedule(Context("workflow_dispatch", "refs/tags/1.2.3", publish, true));
            Assert.AreEqual("success", results["pack-and-validate"]);
            Assert.AreEqual("success", results["verify-release-artifacts"]);
            var calls = RunMockPublish(workflow, results["publish"] == "success");
            Assert.AreEqual(0, calls.Length, "A tag-selected dry run invoked the workflow's publish command.");
            Assert.AreEqual("skipped", results["publish"], "Dry runs must not reach the production approval gate.");
        }

        public static IEnumerable<object[]> UnsuccessfulPrerequisites()
        {
            foreach (var reference in new[] { "refs/tags/1.2.3", "refs/heads/main", "refs/heads/release/1.2", "refs/heads/hotfix/1.2.3" })
            foreach (var job in new[] { "build-and-test", "roslyn-hosts", "pack-and-validate", "verify-release-artifacts" })
            foreach (var result in new[] { "failure", "cancelled", "skipped" })
                yield return new object[] { reference, job, result };
        }

        [TestMethod]
        [DynamicData(nameof(UnsuccessfulPrerequisites))]
        public void UnsuccessfulPrerequisitesBlockPublishing(string reference, string job, string result)
        {
            var workflow = Workflow.Read();
            var eventName = reference.StartsWith("refs/tags/", StringComparison.Ordinal) ? "push" : "workflow_dispatch";
            var context = Context(eventName, reference, true, false);
            Assert.AreEqual("success", workflow.Schedule(context)["publish"], "The control must reach publishing.");
            var results = workflow.Schedule(context, new Dictionary<string, string> { [job] = result });
            Assert.AreEqual("skipped", results["publish"], job + "=" + result);
        }

        [TestMethod]
        [DataRow("push", "refs/tags/1.2.3")]
        [DataRow("push", "refs/tags/1.2.3-beta.1")]
        [DataRow("workflow_dispatch", "refs/heads/main")]
        [DataRow("workflow_dispatch", "refs/heads/release/1.2")]
        [DataRow("workflow_dispatch", "refs/heads/hotfix/1.2.3")]
        public void AuthorizedPublishingUsesTheExactThreePackagesWithMockedDotnet(string eventName, string reference)
        {
            var workflow = Workflow.Read();
            var results = workflow.Schedule(Context(eventName, reference, true, false));
            Assert.AreEqual("success", results["publish"]);
            var calls = RunMockPublish(workflow, true);
            CollectionAssert.AreEqual(new[]
            {
                "nuget push artifacts/packages/Mammoth.LiteMapper.Abstractions.1.2.3.nupkg --source https://api.nuget.org/v3/index.json --api-key local-mock-key",
                "nuget push artifacts/packages/Mammoth.LiteMapper.Generator.1.2.3.nupkg --source https://api.nuget.org/v3/index.json --api-key local-mock-key",
                "nuget push artifacts/packages/Mammoth.LiteMapper.1.2.3.nupkg --source https://api.nuget.org/v3/index.json --api-key local-mock-key"
            }, calls.Select(call => call.Replace('\\', '/')).ToArray());
            StringAssert.Contains(workflow.Job("publish"), "    environment: production");
        }

        [TestMethod]
        [DataRow("schedule")]
        [DataRow("workflow_run")]
        public void OtherEventsCannotAuthorizeTagPublishing(string eventName)
        {
            var workflow = Workflow.Read();
            var context = Context(eventName, "refs/tags/1.2.3", true, false);
            Assert.IsFalse(workflow.Condition("publish", context));
            Assert.AreEqual("skipped", workflow.Schedule(context)["publish"]);
        }

        [TestMethod]
        public void NonManualEventsHaveNoPublishingInputs()
        {
            var context = Context("push", "refs/tags/1.2.3", false, true);
            context["inputs.publish"] = null;
            context["inputs.dry_run"] = null;
            Assert.AreEqual("success", Workflow.Read().Schedule(context)["publish"]);
            context["github.event_name"] = "pull_request";
            Assert.AreEqual("skipped", Workflow.Read().Schedule(context)["publish"]);
        }

        private static Dictionary<string, object?> Context(string eventName, string reference, bool publish, bool dryRun)
        {
            return new Dictionary<string, object?>
            {
                ["github.event_name"] = eventName,
                ["github.ref"] = reference,
                ["github.ref_type"] = reference.StartsWith("refs/tags/", StringComparison.Ordinal) ? "tag" : "branch",
                ["inputs.publish"] = publish,
                ["inputs.dry_run"] = dryRun
            };
        }

        private static string[] RunMockPublish(Workflow workflow, bool scheduled)
        {
            // Run only the extracted push script, with dotnet shadowed by a local function.
            // The genuine dotnet executable and any actual API key are never used here.
            var command = @"
$ErrorActionPreference = 'Stop'
$env:NUGET_API_KEY = 'local-mock-key'
function dotnet { 'MOCK:' + ($args -join ' ') }
";
            if (scheduled)
                command += workflow.PublishScript
                    .Replace("${{ needs.pack-and-validate.outputs.semver }}", "1.2.3", StringComparison.Ordinal)
                    .Replace("${{ env.PACKAGE_OUTPUT }}", "artifacts/packages", StringComparison.Ordinal);
            Assert.IsFalse(command.Contains("${{", StringComparison.Ordinal), "Unresolved workflow interpolation.");
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            var result = TestProcess.Run("pwsh", "-NoProfile -EncodedCommand " + encoded, Repository.Root, TimeSpan.FromMinutes(1));
            Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
            return result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("MOCK:", StringComparison.Ordinal)).Select(line => line.Substring(5)).ToArray();
        }

        private sealed class Workflow
        {
            private readonly Dictionary<string, string> jobs;
            private Workflow(string source)
            {
                var jobStart = Regex.Match(source, @"(?m)^jobs:\r?$");
                Assert.IsTrue(jobStart.Success, "Workflow jobs missing.");
                jobs = Regex.Matches(source.Substring(jobStart.Index + jobStart.Length), @"(?ms)^  (?<id>[a-z][a-z0-9-]*):\r?\n(?<body>.*?)(?=^  [a-z][a-z0-9-]*:|\z)")
                    .ToDictionary(match => match.Groups["id"].Value, match => match.Groups["body"].Value);
                foreach (var id in new[] { "build-and-test", "roslyn-hosts", "pack-and-validate", "verify-release-artifacts", "publish" })
                    Assert.IsTrue(jobs.ContainsKey(id), "Workflow job missing: " + id);
            }

            public static Workflow Read() => new Workflow(File.ReadAllText(Repository.Path(".github/workflows/ci.yml")));
            public string Job(string id) => jobs[id];
            public string PublishScript
            {
                get
                {
                    var match = Regex.Match(Job("publish"), @"(?ms)^      - name: Publish exact packages\r?\n.*?^        run: \|\r?\n(?<script>(?:^          [^\r\n]*\r?\n?)+)");
                    Assert.IsTrue(match.Success, "Publish script missing.");
                    return Regex.Replace(match.Groups["script"].Value, @"(?m)^          ", "");
                }
            }

            public bool Condition(string id, Dictionary<string, object?> context)
            {
                var match = Regex.Match(Job(id), @"(?m)^    if: >\r?\n(?<expression>(?:^      [^\r\n]*\r?\n?)+)");
                Assert.IsTrue(match.Success, "Expected a folded job condition: " + id);
                return new Expression(match.Groups["expression"].Value, context).Evaluate();
            }

            public Dictionary<string, string> Schedule(Dictionary<string, object?> context, Dictionary<string, string>? overrides = null)
            {
                var results = new Dictionary<string, string>();
                foreach (var id in jobs.Keys)
                {
                    var needs = Regex.Match(Job(id), @"(?m)^    needs: (?<needs>[^\r\n]+)").Groups["needs"].Value
                        .Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray();
                    Assert.IsTrue(needs.All(results.ContainsKey), "Prerequisites must precede dependent jobs: " + id);
                    var dependenciesSucceeded = needs.All(need => results[need] == "success");
                    var values = new Dictionary<string, object?>(context)
                    {
                        ["success()"] = dependenciesSucceeded,
                        ["failure()"] = needs.Any(need => results[need] == "failure"),
                        ["cancelled()"] = results.Values.Contains("cancelled"),
                        ["always()"] = true
                    };
                    var explicitStatus = Regex.IsMatch(Job(id).Split("    steps:")[0], @"\b(success|failure|cancelled|always)\(");
                    var scheduled = (explicitStatus || dependenciesSucceeded) &&
                        (!Regex.IsMatch(Job(id), @"(?m)^    if:") || Condition(id, values));
                    results[id] = scheduled ? overrides != null && overrides.TryGetValue(id, out var result) ? result : "success" : "skipped";
                }
                return results;
            }
        }

        // This is deliberately a restricted Actions-expression evaluator, not a YAML/Actions
        // runtime. Unknown tokens/functions fail closed. Boolean inputs stay typed, string
        // comparisons/startsWith ignore case, and absent inputs compare like null/zero.
        // Semantics: https://docs.github.com/en/actions/reference/workflows-and-actions/expressions
        // Needs: https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds
        private sealed class Expression
        {
            private readonly List<string> tokens = new List<string>();
            private readonly Dictionary<string, object?> context;
            private int position;
            public Expression(string source, Dictionary<string, object?> context)
            {
                this.context = context;
                var lexer = new Regex(@"\G\s*('(?:[^']|'')*'|==|!=|&&|\|\||[(),!]|[A-Za-z_][A-Za-z0-9_.-]*)");
                for (var offset = 0; offset < source.TrimEnd().Length;)
                {
                    var match = lexer.Match(source, offset);
                    Assert.IsTrue(match.Success, "Unsupported expression at: " + source.Substring(offset));
                    tokens.Add(match.Groups[1].Value);
                    offset = match.Index + match.Length;
                }
            }

            public bool Evaluate()
            {
                var result = Or();
                Assert.AreEqual(tokens.Count, position, "Expression not fully evaluated.");
                return Truth(result);
            }
            private bool Take(string token)
            {
                if (position >= tokens.Count || tokens[position] != token) return false;
                position++;
                return true;
            }
            private void Require(string token) => Assert.IsTrue(Take(token), "Expected token " + token);
            private object? Or()
            {
                var value = And();
                while (Take("||")) { var right = And(); value = Truth(value) || Truth(right); }
                return value;
            }
            private object? And()
            {
                var value = Equality();
                while (Take("&&")) { var right = Equality(); value = Truth(value) && Truth(right); }
                return value;
            }
            private object? Equality()
            {
                var value = Primary();
                if (Take("==")) return Equal(value, Primary());
                if (Take("!=")) return !Equal(value, Primary());
                return value;
            }
            private object? Primary()
            {
                if (Take("!")) return !Truth(Primary());
                if (Take("(")) { var value = Or(); Require(")"); return value; }
                Assert.IsTrue(position < tokens.Count, "Unexpected expression end.");
                var token = tokens[position++];
                if (token.StartsWith("'", StringComparison.Ordinal)) return token.Substring(1, token.Length - 2).Replace("''", "'");
                if (token == "true") return true;
                if (token == "false") return false;
                if (token == "null") return null;
                if (Take("("))
                {
                    if (token == "startsWith")
                    {
                        var text = Or() as string; Require(","); var prefix = Or() as string; Require(")");
                        Assert.IsNotNull(text); Assert.IsNotNull(prefix);
                        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                    }
                    Require(")");
                    token += "()";
                }
                Assert.IsTrue(context.ContainsKey(token), "Unsupported expression token: " + token);
                return context[token];
            }
            private static bool Truth(object? value) => value is bool boolean ? boolean : value is string text && text.Length != 0;
            private static bool Equal(object? left, object? right)
            {
                if (left is string text && right is string other) return string.Equals(text, other, StringComparison.OrdinalIgnoreCase);
                if (left is bool boolean && right is bool otherBoolean) return boolean == otherBoolean;
                if (left == null || right == null) return !Truth(left) && !Truth(right);
                return false;
            }
        }
    }
}
