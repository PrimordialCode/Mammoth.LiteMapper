using System;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mammoth.LiteMapper.Generator.Tests
{
    [TestClass]
    public sealed class AllocationTestProcessTests
    {
        private const string PositiveControlFailureMessage = "The allocation-process positive control must reject a real allocation.";
        private static byte[]? _escapedAllocation;

        [TestMethod]
        [TestCategory("Allocation")]
        public void ParentConcurrentGcSettingIsUnchangedWhenChildDisablesConcurrentGc()
        {
            Assert.IsTrue(GetConcurrentGcSetting(), "The parent test process must retain the default concurrent GC setting.");

            Assert.IsTrue(AllocationTestProcess.Run(typeof(AllocationTestProcessTests), nameof(VerifyChildSettings)));

            Assert.IsTrue(GetConcurrentGcSetting(), "Launching an allocation-test child must not change the parent GC setting.");
        }

        [TestMethod]
        [TestCategory("Allocation")]
        [DataRow(false)]
        [DataRow(true)]
        public void NativeAllocationCounterDistinguishesNoWorkFromAnEscapedArray(bool allocate)
        {
            Assert.IsTrue(AllocationTestProcess.Run(
                typeof(AllocationTestProcessTests),
                nameof(VerifyNativeAllocationCounter),
                allocate));
        }

        [TestMethod]
        [TestCategory("Allocation")]
        public void ChildAllocationAssertionFailurePropagatesThroughProcessBoundary()
        {
            var failure = Assert.ThrowsExactly<AssertFailedException>(() =>
            {
                AllocationTestProcess.Run(typeof(AllocationTestProcessTests), nameof(VerifyPositiveControlFailure));
            });

            StringAssert.Contains(failure.Message, PositiveControlFailureMessage);
            StringAssert.Contains(failure.Message, "64");
        }

        private static void VerifyChildSettings()
        {
            Assert.IsTrue(AllocationTestProcess.IsChild, "The verification method must run in the isolated child process.");
            Assert.IsFalse(GetConcurrentGcSetting(), "Allocation-test children must disable concurrent GC before running the selected method.");
        }

        private static void VerifyNativeAllocationCounter(bool allocate)
        {
            WarmUpKnownAllocation();
            _ = GC.GetAllocatedBytesForCurrentThread();

            var before = GC.GetAllocatedBytesForCurrentThread();
            if (allocate)
            {
                CreateEscapedKnownAllocation();
            }

            var actual = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(_escapedAllocation);

            Assert.AreEqual(allocate ? 64L : 0L, actual, "The native thread allocation counter must expose exact, noise-free measurements in the isolated child.");
        }

        private static void VerifyPositiveControlFailure()
        {
            WarmUpKnownAllocation();
            _ = GC.GetAllocatedBytesForCurrentThread();

            var before = GC.GetAllocatedBytesForCurrentThread();
            CreateEscapedKnownAllocation();
            var actual = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(_escapedAllocation);

            Assert.AreEqual(0L, actual, PositiveControlFailureMessage);
        }

        private static bool GetConcurrentGcSetting()
        {
            return (bool)GC.GetConfigurationVariables()["ConcurrentGC"];
        }

        private static void WarmUpKnownAllocation()
        {
            for (var index = 0; index < 16; index++)
            {
                CreateEscapedKnownAllocation();
            }

            _escapedAllocation = null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CreateEscapedKnownAllocation()
        {
            _escapedAllocation = new byte[37];
        }
    }
}
