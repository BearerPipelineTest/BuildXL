// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips
{
    /// <summary>
    /// Result of executing a pip.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct PipResult
    {
        /// <summary>
        /// Singleton result for <see cref="PipResultStatus.Skipped"/>. This result does not carry any performance info.
        /// </summary>
        public static readonly PipResult Skipped = CreateForNonExecution(PipResultStatus.Skipped);

        /// <nodoc />
        public readonly PipResultStatus Status;

        /// <nodoc />
        public readonly bool MustBeConsideredPerpetuallyDirty;

        /// <nodoc />
        public readonly int ExitCode;

        /// <nodoc />
        public readonly PipExecutionPerformance PerformanceInfo;

        /// <nodoc />
        public readonly ReadOnlyArray<AbsolutePath> DynamicallyObservedFiles;

        /// <nodoc />
        public readonly ReadOnlyArray<AbsolutePath> DynamicallyProbedFiles;

        /// <nodoc />
        public readonly ReadOnlyArray<AbsolutePath> DynamicallyObservedEnumerations;

        /// <nodoc />
        public readonly ReadOnlyArray<AbsolutePath> DynamicallyObservedAbsentPathProbes;

        /// <nodoc />
        public bool HasDynamicObservations =>
            DynamicallyObservedFiles.Length > 0
            || DynamicallyProbedFiles.Length > 0
            || DynamicallyObservedEnumerations.Length > 0
            || DynamicallyObservedAbsentPathProbes.Length > 0;

        /// <nodoc />
        public PipResult(
            PipResultStatus status,
            PipExecutionPerformance performanceInfo,
            bool mustBeConsideredPerpetuallyDirty,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedFiles,
            ReadOnlyArray<AbsolutePath> dynamicallyProbedFiles,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedEnumerations,
            ReadOnlyArray<AbsolutePath> dynamicallyObservedAbsentPathProbes,
            int exitCode)
        {
            Contract.Requires(!status.IndicatesExecution() == (performanceInfo == null));
            Contract.Requires(dynamicallyObservedFiles.IsValid);
            Contract.Requires(dynamicallyProbedFiles.IsValid);
            Contract.Requires(dynamicallyObservedEnumerations.IsValid);
            Contract.Requires(dynamicallyObservedAbsentPathProbes.IsValid);

            Status = status;
            PerformanceInfo = performanceInfo;
            MustBeConsideredPerpetuallyDirty = mustBeConsideredPerpetuallyDirty;
            DynamicallyObservedFiles = dynamicallyObservedFiles;
            DynamicallyProbedFiles = dynamicallyProbedFiles;
            DynamicallyObservedEnumerations = dynamicallyObservedEnumerations;
            DynamicallyObservedAbsentPathProbes = dynamicallyObservedAbsentPathProbes;
            ExitCode = exitCode;
        }

        /// <summary>
        /// Creates a <see cref="PipResult"/> with the given status. The performance info is populated
        /// with zero duration (start / stop right now) and no dynamic observed files or enumerations
        /// </summary>
        public static PipResult CreateWithPointPerformanceInfo(PipResultStatus status, bool mustBeConsideredPerpetuallyDirty = false, int exitCode = 0)
        {
            Contract.Requires(status.IndicatesExecution());
            return new PipResult(
                status,
                PipExecutionPerformance.CreatePoint(status),
                mustBeConsideredPerpetuallyDirty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty,
                exitCode);
        }

        /// <summary>
        /// Creates a <see cref="PipResult"/> with the given status. The performance info is populated
        /// as a duration from <paramref name="executionStart"/> to now without any dynamic observed files or enumerations
        /// </summary>
        public static PipResult Create(PipResultStatus status, DateTime executionStart, bool mustBeConsideredPerpetuallyDirty = false, int exitCode = 0)
        {
            Contract.Requires(status.IndicatesExecution());
            Contract.Requires(executionStart.Kind == DateTimeKind.Utc);
            return new PipResult(
                status,
                PipExecutionPerformance.Create(status, executionStart),
                mustBeConsideredPerpetuallyDirty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty,
                exitCode);
        }

        /// <summary>
        /// Creates a <see cref="PipResult"/> indicating that a pip wasn't actually executed. No performance info is attached.
        /// </summary>
        public static PipResult CreateForNonExecution(PipResultStatus status, bool mustBeConsideredPerpetuallyDirty = false, int exitCode = 0)
        {
            Contract.Requires(!status.IndicatesExecution());
            return new PipResult(
                status,
                null,
                mustBeConsideredPerpetuallyDirty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty,
                ReadOnlyArray<AbsolutePath>.Empty,
                exitCode);
        }
    }

    /// <summary>
    /// Summary result of running a pip.
    /// </summary>
    public enum PipResultStatus : byte
    {
        /// <summary>
        /// The pip executed and succeeded.
        /// </summary>
        Succeeded,

        /// <summary>
        /// The pip decided that it did not need to execute or copy outputs from cache, since its outputs
        /// were already up to date.
        /// </summary>
        UpToDate,

        /// <summary>
        /// The correct output content was not already present, but was deployed from a cache rather than produced a new one.
        /// </summary>
        DeployedFromCache,

        /// <summary>
        /// The pip cannot run from cache, and it is not executed.
        /// </summary>
        NotCachedNotExecuted,

        /// <summary>
        /// The pip can run from cache, but it defers materialization of the correct outputs, i.e., no deployment from cache.
        /// </summary>
        NotMaterialized,

        /// <summary>
        /// The pip attempted to execute, but failed.
        /// </summary>
        Failed,

        /// <summary>
        /// The scheduler decides that pip had to be skipped.
        /// </summary>
        Skipped,

        /// <summary>
        /// Execution of this pip was canceled (after being ready and queued).
        /// </summary>
        Canceled,
    }

    /// <summary>
    /// Extensions for <see cref="PipResultStatus" />
    /// </summary>
    public static class PipResultStatusExtensions
    {
        /// <summary>
        /// Indicates if a pip's result indicates that it failed.
        /// </summary>
        public static bool IndicatesFailure(this PipResultStatus result)
        {
            // Note: Skipped pips are not considered failures because they do not by themselves cause a build to fail.
            // If a pip is skipped due to an upstream failure, that upstream pip is the one that causes the session to fail.
            return result == PipResultStatus.Failed || result == PipResultStatus.Canceled;
        }

        /// <summary>
        /// Indicates that a pip has no outputs.
        /// </summary>
        /// <remarks>
        /// The <see cref="PipResultStatus.NotMaterialized"/> status is not included because it indicates that the pip has some output, only that
        /// the outputs are not materialized. The <see cref="PipResultStatus.NotCachedNotExecuted"/> is not included because it doesn't seem to be
        /// used anywhere.
        /// </remarks>
        public static bool IndicatesNoOutput(this PipResultStatus result) => result.IndicatesFailure() || result == PipResultStatus.Skipped;

        /// <summary>
        /// Indicates if a pip's result indications some level of execution, though possibly just an up-to-date check (i.e., not skipped entirely).
        /// </summary>
        public static bool IndicatesExecution(this PipResultStatus result)
        {
            return result == PipResultStatus.UpToDate || result == PipResultStatus.NotMaterialized || result == PipResultStatus.DeployedFromCache ||
                   result == PipResultStatus.Succeeded || result == PipResultStatus.Failed || result == PipResultStatus.Canceled;
        }

        /// <summary>
        /// Converts this result to a value indicating execution vs. cache status.
        /// The result must indicate execution (see <see cref="IndicatesExecution"/>).
        /// </summary>
        public static PipExecutionLevel ToExecutionLevel(this PipResultStatus result)
        {
            Contract.Requires(result.IndicatesExecution());

            switch (result)
            {
                case PipResultStatus.Succeeded:
                    return PipExecutionLevel.Executed;
                case PipResultStatus.Failed:
                case PipResultStatus.Canceled:
                    return PipExecutionLevel.Failed;
                case PipResultStatus.DeployedFromCache:
                case PipResultStatus.NotMaterialized: // TODO: This is misleading; should account for eventual materialization.
                    return PipExecutionLevel.Cached;
                case PipResultStatus.UpToDate:
                    return PipExecutionLevel.UpToDate;
                default:
                    throw Contract.AssertFailure("Unhandled Pip Result that indicates execution");
            }
        }
    }
}
