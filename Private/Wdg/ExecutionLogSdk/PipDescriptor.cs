// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine;
using BuildXL.Pips;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Describes one pip from the build graph.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification = "This class should not be called a Collection")]
    [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase", Justification = "Path strings are all lower case in the SDK")]
    public sealed class PipDescriptor
    {
        #region Private properties

        /// <summary>
        /// The total number of dependent pips (transitively)
        /// </summary>
        private readonly Lazy<ConcurrentHashSet<PipDescriptor>> m_transitiveDependentPips;

        /// <summary>
        /// Critical path based on the number of files produced from cache
        /// </summary>
        private readonly Lazy<PipDescriptor> m_criticalPathBasedOnNumberOfPipsProducedFromCache;

        /// <summary>
        /// The number of files produced from cache on the critical path starting from this pip
        /// </summary>
        private readonly Lazy<int> m_numberOfFilesProducedFromCacheOnCriticalPath;

        /// <summary>
        /// Critical path based on execution time
        /// </summary>
        private readonly Lazy<PipDescriptor> m_criticalPathBasedOnExecutionTime;

        /// <summary>
        /// The total execution time on the critical path
        /// </summary>
        private readonly Lazy<int> m_criticalPathLength;

        /// <summary>
        /// The length of the pip's dependency chain based on the number of dependent pips (only counting Process pips in the chain)
        /// </summary>
        private readonly Lazy<int> m_dependencyChainLength;

        /// <summary>
        /// Static build graph loaded from the execution log
        /// </summary>
        private CachedGraph m_buildGraph;

        /// <summary>
        /// Holds most of the data for this pip
        /// </summary>
        private Process m_fullPip;
        #endregion

        #region Internal properties

        /// <summary>
        /// Internal collection containing the output files that are being generated by the pip
        /// </summary>
        internal readonly ConcurrentHashSet<FileDescriptor> OutputFilesHashset = null;

        /// <summary>
        /// Internal collection containing the files that the pip depends on
        /// </summary>
        internal readonly ConcurrentHashSet<FileDescriptor> DependentFilesHashset = null;

        /// <summary>
        /// Internal collection containing the files that the pip has accessed during the build.
        /// </summary>
        internal readonly ConcurrentHashSet<FileDescriptor> ObservedInputsHashset = null;

        /// <summary>
        /// Internal collection containing the files that the pip has accessed during the build.
        /// </summary>
        internal readonly ConcurrentHashSet<FileDescriptor> ProbedFilesHashset = null;

        /// <summary>
        /// Internal collection containing the directories that the pip has accessed during the build.
        /// </summary>
        internal readonly ConcurrentHashSet<DirectoryDescriptor> DirectoryOutputsHashset = new ConcurrentHashSet<DirectoryDescriptor>();

        /// <summary>
        /// Internal collection containing the directories that the pip has accessed during the build.
        /// </summary>
        internal readonly ConcurrentHashSet<DirectoryDescriptor> DirectoryDependenciesHashset = new ConcurrentHashSet<DirectoryDescriptor>();

        /// <summary>
        /// Internal collection containing the environment variables and their values as seen by the process that has executed the pip.
        /// </summary>
        internal readonly StringIdEnvVarDictionary EnvironmentVariablesDictionary = null;

        /// <summary>
        /// Internal collection containing external file dependencies that are not tracked by the BuildXL scheduler.
        /// </summary>
        internal readonly AbsolutePathConcurrentHashSet UntrackedPathsHashset = null;

        /// <summary>
        /// Internal collection containing external directory dependencies that are not tracked by the BuildXL scheduler.
        /// </summary>
        internal readonly AbsolutePathConcurrentHashSet UntrackedScopesHashset = null;

        /// <summary>
        /// Internal collection containing the pips that are directly dependent on the current pip.
        /// </summary>
        internal readonly ConcurrentHashSet<PipDescriptor> AdjacentInNodesHashset = null;

        /// <summary>
        /// Internal collection containing the pips that are directly dependent on the current pip.
        /// </summary>
        internal readonly ConcurrentHashSet<PipDescriptor> AdjacentOutNodesHashset = null;

        /// <summary>
        /// Internal collection containing process descriptors for all processes launched by the current pip (directly or indirectly)
        /// </summary>
        internal readonly ConcurrentHashSet<ProcessInstanceDescriptor> ReportedProcessesHashset = null;

        /// <summary>
        /// Flag that signals that the pip descriptor has been initialized and ready to be used.
        /// </summary>
        internal int IsInitializedFlag;

        /// <summary>
        /// List of tags assigned to the pip
        /// </summary>
        internal StringIdConcurrentHashSet PipTags = null;
        #endregion

        #region Public properties

        /// <summary>
        /// Unique pip identifier that is assigned to every pip by BuildXL.
        /// </summary>
        public uint PipId => m_fullPip.PipId.Value;

        /// <summary>
        /// BuildXL provided node id used to identify the pip in the build graph.
        /// </summary>
        public NodeId NodeId => m_fullPip.PipId.ToNodeId();

        /// <summary>
        /// Pip identifier that is stable across BuildXL runs with an identical schedule.
        /// </summary>
        /// <remarks>
        /// This identifier is not guaranteed to be unique, but should be unique in practice.
        /// </remarks>
        public long SemiStableHash => m_fullPip.SemiStableHash;

        /// <summary>
        /// The type of the pip. The SDK only loads process pips, therefore the value of this property is always PipType.Process.
        /// </summary>
        /// <see cref="PipType"/>
        public PipType PipType => m_fullPip.PipType;

        /// <summary>
        /// The name of the pip
        /// </summary>
        /// <remarks>
        /// This name is not necessarily unique throughout the build.
        /// </remarks>
        public string PipName => PipNameId.ToString(m_buildGraph.Context.SymbolTable);

        /// <summary>
        /// The FullSymbol value for the PipName
        /// </summary>
        public FullSymbol PipNameId => m_fullPip.Provenance.OutputValueSymbol;

        /// <summary>
        /// A human-readable representation of the build qualifier.
        /// </summary>
        public string QualifierName => m_buildGraph.Context.QualifierTable.GetCanonicalDisplayString(m_fullPip.Provenance.QualifierId);

        /// <summary>
        /// The StringId for the QualifierName
        /// </summary>
        public StringId QualifierNameId => StringId.Create(m_buildGraph.Context.StringTable, QualifierName);

        /// <summary>
        /// An optional string that describes the particular details of this pip.
        /// </summary>
        public string Usage => m_fullPip.Provenance.Usage.IsValid ? m_fullPip.Provenance.Usage.ToString(m_buildGraph.Context.PathTable) : string.Empty;

        /// <summary>
        /// The name of the BuildXL spec file that defines the pip.
        /// </summary>
        public string DominoSpecFile => m_buildGraph.Context.PathTable.AbsolutePathToString(DominoSpecFileId);

        /// <summary>
        /// The AbsolutePath value for the BuildXLSpecFile
        /// </summary>
        public AbsolutePath DominoSpecFileId => m_fullPip.Provenance.Token.Path;

        /// <summary>
        /// Module descriptor that identifies the module that the pip belongs to.
        /// </summary>
        public ModuleDescriptor Module { get; internal set; }

        /// <summary>
        /// The directory where the pip is executed.
        /// </summary>
        public string WorkingDirectory => m_buildGraph.Context.PathTable.AbsolutePathToString(WorkingDirectoryId);

        /// <summary>
        /// The AbsolutePath value for the WorkingDirectory
        /// </summary>
        public AbsolutePath WorkingDirectoryId => m_fullPip.WorkingDirectory;

        /// <summary>
        /// The length of the pip's dependency chain (only counting the Process pips in the chain).
        /// </summary>
        public int DependencyChainLength { get { return m_dependencyChainLength.Value; } }

        /// <summary>
        /// Collection of process pips that the current pip depends on (directly or indirectly).
        /// </summary>
        public IReadOnlyCollection<PipDescriptor> TransitiveDependentPips { get { return m_transitiveDependentPips.Value; } }

        /// <summary>
        /// The next pip on the critical path. The critical path is based on the number of pips who's outputs have been produced from cache.
        /// </summary>
        public PipDescriptor CriticalPathBasedOnNumberOfPipsProducedFromCache { get { return m_criticalPathBasedOnNumberOfPipsProducedFromCache.Value; } }

        /// <summary>
        /// The total number of files produced from cache by pips that are on the critical path starting from the current pip.
        /// The critical path is based on the number of pips who's outputs have been produced from cache.
        /// </summary>
        public int NumberOfFilesProducedFromCacheOnCriticalPath { get { return m_numberOfFilesProducedFromCacheOnCriticalPath.Value; } }

        /// <summary>
        /// The length of the pip's dependency chain based on the number of dependent pips when considering the whole build graph,
        /// not just the build graph containing Process pips only.
        /// </summary>
        public int NodeHeight => m_buildGraph.DirectedGraph.GetNodeHeight(NodeId);

        /// <summary>
        /// The amount of time (in milliseconds) it takes to execute the pips on the critical path starting from the current pip.
        /// The critical path is the dependency chain with the longest execution time.
        /// </summary>
        public int CriticalPathLength => m_criticalPathLength.Value;

        /// <summary>
        /// The next pip on the critical path. The critical path is the dependency chain with the longest execution time.
        /// </summary>
        public PipDescriptor CriticalPath => m_criticalPathBasedOnExecutionTime.Value;

        /// <summary>
        /// The tool to launch when running the pip.
        /// </summary>
        public FileDescriptor Executable { get; internal set; }

        /// <summary>
        /// The name of the tool associated with the pip, or empty string if the pip doesn't map to a tool.
        /// </summary>
        public string ToolName => ToolNameId.ToString(m_buildGraph.Context.StringTable);

        /// <summary>
        /// The StringId for the ToolName
        /// </summary>
        public StringId ToolNameId => m_fullPip.GetToolName(m_buildGraph.Context.PathTable).StringId;

        /// <summary>
        /// The description of the tool associated with the pip, or empty string if the pip doesn't map to a tool.
        /// </summary>
        public string ToolDescription => ToolDescriptionId.ToString(m_buildGraph.Context.StringTable);

        /// <summary>
        /// The StringId for the ToolDescription
        /// </summary>
        public StringId ToolDescriptionId => m_fullPip.ToolDescription;

        /// <summary>
        /// The name of the file that receives the pip's standard input.
        /// </summary>
        public string StandardInputFile => m_buildGraph.Context.PathTable.AbsolutePathToString(StandardInputFileId);

        /// <summary>
        /// The AbsolutePath for the StandardInputFile
        /// </summary>
        public AbsolutePath StandardInputFileId => m_fullPip.StandardInput.File.Path;

        /// <summary>
        /// Data that has been passed to the pip's standard input.
        /// </summary>
        public string StandardInputData => m_fullPip.StandardInput.IsData ? m_fullPip.StandardInput.Data.ToString(m_buildGraph.Context.PathTable) : null;

        /// <summary>
        /// The name of the file that receives the pip's standard output.
        /// </summary>
        public string StandardOutput => m_buildGraph.Context.PathTable.AbsolutePathToString(StandardOutputId);

        /// <summary>
        /// The AbsolutePath for the StandardOutput
        /// </summary>
        public AbsolutePath StandardOutputId => m_fullPip.StandardOutput.Path;

        /// <summary>
        /// The name of the file that receives the pip's standard directory.
        /// </summary>
        public string StandardDirectory => m_buildGraph.Context.PathTable.AbsolutePathToString(StandardDirectoryId);

        /// <summary>
        /// The AbsolutePath for the StandardDirectory
        /// </summary>
        public AbsolutePath StandardDirectoryId => m_fullPip.StandardDirectory;

        /// <summary>
        /// The name of the file that receives the pip's standard error.
        /// </summary>
        public string StandardError => m_buildGraph.Context.PathTable.AbsolutePathToString(StandardErrorId);

        /// <summary>
        /// The AbsolutePath for the StandardError
        /// </summary>
        public AbsolutePath StandardErrorId => m_fullPip.StandardError.Path;

        /// <summary>
        /// Signals that the pip has nested processes that BuildXL did not track as dependencies.
        /// </summary>
        public bool HasUntrackedChildProcesses => m_fullPip.HasUntrackedChildProcesses;

        /// <summary>
        /// An interval value that indicates after which time a tool was issuing warnings that it is running longer than
        /// anticipated.
        /// </summary>
        public long WarningTimeout => m_fullPip.WarningTimeout.HasValue ? m_fullPip.WarningTimeout.Value.Ticks : -1;

        /// <summary>
        /// A hard timeout after which the Process was marked as failing due to timeout and terminated.
        /// </summary>
        public long Timeout => m_fullPip.Timeout.HasValue ? m_fullPip.Timeout.Value.Ticks : -1;

        /// <summary>
        /// Optional regular expression to detect warnings in console error / output.
        /// </summary>
        public string WarningRegexPattern => WarningRegexPatternId.ToString(m_buildGraph.Context.StringTable);

        /// <summary>
        /// The StringId for the WarningRegexPattern
        /// </summary>
        public StringId WarningRegexPatternId => m_fullPip.WarningRegex.IsValid ? m_fullPip.WarningRegex.Pattern : StringId.Invalid;

        /// <summary>
        /// A bitwise combination of the options that modify WarningRegexPattern
        /// </summary>
        public RegexOptions WarningRegexOptions => m_fullPip.WarningRegex.IsValid ? m_fullPip.WarningRegex.Options : default(RegexOptions);

        /// <summary>
        /// The Arguments assigned to the pip's process.
        /// </summary>
        public string Arguments => m_fullPip.Arguments.ToString(m_buildGraph.Context.PathTable);

        /// <summary>
        /// The fingerprint hash value of the pip
        /// </summary>
        public ContentHash Fingerprint { get; internal set; }

        /// <summary>
        /// The fingerprint computation kind
        /// </summary>
        public FingerprintComputationKind FingerprintKind { get; internal set; }

        /// <summary>
        /// Strong fingerprint computations
        /// </summary>
        public IReadOnlyList<ProcessStrongFingerprintComputationData> StrongFingerprintComputations;

        /// <summary>
        /// Pip execution performance metrics.
        /// </summary>
        /// <see cref="ProcessPipExecutionPerformance"/>
        public ProcessPipExecutionPerformance PipExecutionPerformance { get; internal set; }

        /// <summary>
        /// The output files that are being generated by the pip
        /// </summary>
        /// <remarks>
        /// The content of this collection is based on static data extracted from the static build graph.
        /// When a file is listed here it means that the pip generates this file either through
        /// executing a command or by getting the file from cache.
        /// Check <see cref="WasExecuted"/> to determine if the pip ran or populated its outputs from cache.
        /// </remarks>
        public IReadOnlyCollection<FileDescriptor> OutputFiles
        {
            get { return OutputFilesHashset; }
        }

        /// <summary>
        /// The files that the pip depends on
        /// </summary>
        /// <remarks>
        /// This collection includes source file dependencies extracted
        /// from the static build graph and does not include observed file accesses detected during the execution of the pip.
        /// Check <see cref="ObservedInputs"/> for observed inputs.
        /// </remarks>
        public IReadOnlyCollection<FileDescriptor> DependentFiles
        {
            get { return DependentFilesHashset; }
        }

        /// <summary>
        /// The files that the pip has accessed during the build without explicitly declaring it as a dependency.
        /// </summary>
        public IReadOnlyCollection<FileDescriptor> ObservedInputs
        {
            get { return ObservedInputsHashset; }
        }

        /// <summary>
        /// Collection containing the files that the pip has probed during the build (and the file did not exist).
        /// </summary>
        public IReadOnlyCollection<FileDescriptor> ProbedFiles
        {
            get { return ProbedFilesHashset; }
        }

        /// <summary>
        /// The directories that the pip produced outputs into.
        /// </summary>
        public IReadOnlyCollection<DirectoryDescriptor> DirectoryOutputs
        {
            get { return DirectoryOutputsHashset; }
        }

        /// <summary>
        /// The directories that the pip has accessed (enumerated or read files from) during the build.
        /// </summary>
        public IReadOnlyCollection<DirectoryDescriptor> DirectoryDependencies
        {
            get { return DirectoryDependenciesHashset; }
        }

        /// <summary>
        /// The environment variables and their values as seen by the process that has executed the pip.
        /// </summary>
        public IReadOnlyDictionary<string, string> EnvironmentVariables
        {
            get { return EnvironmentVariablesDictionary; }
        }

        /// <summary>
        /// External file dependencies that are no tracked by the BuildXL scheduler.
        /// </summary>
        public IReadOnlyCollection<string> UntrackedPaths
        {
            get { return UntrackedPathsHashset; }
        }

        /// <summary>
        /// External directory dependencies that are no tracked by the BuildXL scheduler.
        /// </summary>
        public IReadOnlyCollection<string> UntrackedScopes
        {
            get { return UntrackedScopesHashset; }
        }

        /// <summary>
        /// The exit codes that represent success.
        /// </summary>
        public IReadOnlyCollection<int> SuccessExitCodes
        {
            get { return m_fullPip.SuccessExitCodes; }
        }

        /// <summary>
        /// The pips that the current pip directly dependents on.
        /// </summary>
        public IReadOnlyCollection<PipDescriptor> AdjacentInNodes
        {
            get { return AdjacentInNodesHashset; }
        }

        /// <summary>
        /// The pips that are directly dependent on the current pip.
        /// </summary>
        public IReadOnlyCollection<PipDescriptor> AdjacentOutNodes
        {
            get { return AdjacentOutNodesHashset; }
        }

        /// <summary>
        /// Process descriptors for all processes launched by the current pip (directly or indirectly)
        /// </summary>
        /// <remarks>
        /// In order for this collection to be populated the /logProcesses BuildXL flag is required
        /// </remarks>
        public IReadOnlyCollection<ProcessInstanceDescriptor> ReportedProcesses
        {
            get { return ReportedProcessesHashset; }
        }

        /// <summary>
        /// Flag that signals that the pip descriptor has been initialized and ready to be used.
        /// </summary>
        public bool IsInitialized { get { return IsInitializedFlag != 0; } }

        /// <summary>
        /// Flag that signals that the pip has ran during the build (the pip either executed, failed, was up to date or its outputs have been deployed from cache).
        /// </summary>
        public bool WasExecuted { get { return PipExecutionPerformance != null; } }

        /// <summary>
        /// Flag that signals that the pip ran and its output has been built (not pulled from cache and was not up to date).
        /// </summary>
        public bool WasItBuilt { get { return (PipExecutionPerformance != null) && (PipExecutionPerformance.ExecutionLevel == PipExecutionLevel.Executed); } }

        /// <summary>
        /// Flag that signals that the pip ran and its output has been up to date.
        /// </summary>
        public bool WasItUpToDate { get { return (PipExecutionPerformance != null) && (PipExecutionPerformance.ExecutionLevel == PipExecutionLevel.UpToDate); } }

        /// <summary>
        /// Flag that signals that the pip ran and it failed building.
        /// </summary>
        public bool HasFailed { get { return (PipExecutionPerformance != null) && (PipExecutionPerformance.ExecutionLevel == PipExecutionLevel.Failed); } }

        /// <summary>
        /// Flag that signals that the pip has been pulled from cache.
        /// </summary>
        public bool WasDeployedFromCache { get { return (PipExecutionPerformance != null) && (PipExecutionPerformance.ExecutionLevel == PipExecutionLevel.Cached); } }

        /// <summary>
        /// List of tags assigned to the pip
        /// </summary>
        public IReadOnlyCollection<string> Tags => PipTags;

        /// <summary>
        /// The name of the response file (if any) used when executing the pip.
        /// </summary>
        public string ResponseFile => m_buildGraph.Context.PathTable.AbsolutePathToString(ResponseFileId);

        /// <summary>
        /// The AbsolutePath for the ResponseFile
        /// </summary>
        public AbsolutePath ResponseFileId => m_fullPip.ResponseFile.Path;

        /// <summary>
        /// Response file content (if any).
        /// </summary>
        public string ResponseFileData => m_fullPip.ResponseFileData.IsValid ? m_fullPip.ResponseFileData.ToString(m_buildGraph.Context.PathTable)
                        : string.Empty;
        #endregion

        #region Private methods

        /// <summary>
        /// Method used to lazy-initialize the TransitiveDependentPips property
        /// </summary>
        /// <returns>Collection containing all the pips that are transitively dependent on the current pip</returns>
        private ConcurrentHashSet<PipDescriptor> GetTransitiveDependentPips()
        {
            ConcurrentHashSet<PipDescriptor> transitivePips = new ConcurrentHashSet<PipDescriptor>();
            foreach (var p in AdjacentOutNodesHashset)
            {
                if (!transitivePips.Contains(p))
                {
                    transitivePips.Add(p);
                    transitivePips.AddRange(p.TransitiveDependentPips);
                }
            }

            return transitivePips;
        }

        /// <summary>
        /// Method used to lazy-initialize the NumberOfFilesProducedFromCacheOnCriticalPath property.
        /// </summary>
        /// <returns>Returns the number of files produced from cache on the critical path starting from this pip</returns>
        private int FindNumberOfFilesProducedFromCacheOnCriticalPath()
        {
            return (WasDeployedFromCache ? OutputFiles.Count : 0) + ((AdjacentOutNodes.Count > 0) ? AdjacentOutNodes.Max(p => p.NumberOfFilesProducedFromCacheOnCriticalPath) : 0);
        }

        /// <summary>
        /// Method used to lazy-initialize the CriticalPathBasedOnNumberOfPipsProducedFromCache property.
        /// </summary>
        /// <returns>Returns a reference to the one of this pip's dependent pip with the most outputs produced from cache</returns>
        private PipDescriptor FindCriticalPathBasedOnNumberOfPipsProducedFromCache()
        {
            return AdjacentOutNodes.OrderByDescending(p => p.NumberOfFilesProducedFromCacheOnCriticalPath).ThenByDescending(p => p.DependencyChainLength).FirstOrDefault();
        }

        /// <summary>
        /// Method used to lazy-initialize the CriticalPathBasedExecutionTime property.
        /// </summary>
        /// <returns>Returns a reference to the one of this pip's dependent pip with the longest transitive execution time</returns>
        private PipDescriptor FindCriticalPathBasedOnExecutionTime()
        {
            // Order critical paths based on their execution duration and when there are more than one paths with the same duration, use the number of pips in the critical path
            return AdjacentOutNodes.OrderByDescending(p => p.CriticalPathLength).ThenByDescending(p => p.DependencyChainLength).FirstOrDefault();
        }

        /// <summary>
        /// Method used to lazy-initialize the DependencyChainLength property.
        /// </summary>
        /// <returns>Returns the number of pips on the critical path based on execution time</returns>
        private int FindDependencyChainLength()
        {
            int dependencyChainLength = 1;
            if (CriticalPath != null)
            {
                dependencyChainLength += CriticalPath.DependencyChainLength;
            }

            return dependencyChainLength;
        }

        /// <summary>
        /// Method used to lazy-initialize the CriticalPathLength property.
        /// </summary>
        /// <returns>Returns the transitive execution time on the critical path</returns>
        private int FindCriticalPathLength()
        {
            int criticalPathLength = 0;
            if (PipExecutionPerformance != null)
            {
                criticalPathLength += (int)(PipExecutionPerformance.ExecutionStop - PipExecutionPerformance.ExecutionStart).TotalMilliseconds;
            }

            if (CriticalPath != null)
            {
                criticalPathLength += CriticalPath.CriticalPathLength;
            }

            return criticalPathLength;
        }
        #endregion

        #region Internal methods

        /// <summary>
        /// Internal constructor
        /// </summary>
        /// <param name="pipId">The pipId of the pip that this descriptor is assigned to</param>
        internal PipDescriptor(Process fullPip, CachedGraph buildGraph, ExecutionLogLoadOptions loadOptions, ConcurrentHashSet<FileDescriptor> emptyConcurrentHashSetOfFileDescriptor, ConcurrentHashSet<PipDescriptor> emptyConcurrentHashSetOfPipDescriptor,
            ConcurrentHashSet<ProcessInstanceDescriptor> emptyConcurrentHashSetOfReportedProcesses, StringIdEnvVarDictionary emptyStringIDEnvVarDictionary, AbsolutePathConcurrentHashSet emptyAbsolutePathConcurrentHashSet)
        {
            Contract.Requires(fullPip != null);
            Contract.Requires(buildGraph != null);

            // IsInitializedFlag will be set to non 0 when all the pip properties have been set
            IsInitializedFlag = 0;
            PipExecutionPerformance = null;
            m_transitiveDependentPips = Lazy.Create(() => GetTransitiveDependentPips());
            m_criticalPathBasedOnNumberOfPipsProducedFromCache = Lazy.Create(() => FindCriticalPathBasedOnNumberOfPipsProducedFromCache());
            m_numberOfFilesProducedFromCacheOnCriticalPath = Lazy.Create(() => FindNumberOfFilesProducedFromCacheOnCriticalPath());

            m_criticalPathBasedOnExecutionTime = Lazy.Create(() => FindCriticalPathBasedOnExecutionTime());
            m_dependencyChainLength = Lazy.Create(() => FindDependencyChainLength());
            m_criticalPathLength = Lazy.Create(() => FindCriticalPathLength());
            m_buildGraph = buildGraph;
            m_fullPip = fullPip;

            PipTags = new StringIdConcurrentHashSet(m_buildGraph.Context.StringTable);
            foreach (var tag in m_fullPip.Tags)
            {
                if (tag.IsValid)
                {
                    PipTags.Add(tag);
                }
            }

            if ((loadOptions & ExecutionLogLoadOptions.DoNotLoadOutputFiles) == 0)
            {
                OutputFilesHashset = new ConcurrentHashSet<FileDescriptor>();
            }
            else
            {
                OutputFilesHashset = emptyConcurrentHashSetOfFileDescriptor;
            }

            if ((loadOptions & ExecutionLogLoadOptions.DoNotLoadSourceFiles) == 0)
            {
                DependentFilesHashset = new ConcurrentHashSet<FileDescriptor>();
                ProbedFilesHashset = new ConcurrentHashSet<FileDescriptor>();
            }
            else
            {
                DependentFilesHashset = emptyConcurrentHashSetOfFileDescriptor;
                ProbedFilesHashset = emptyConcurrentHashSetOfFileDescriptor;
            }

            if ((loadOptions & ExecutionLogLoadOptions.LoadObservedInputs) == 0)
            {
                ObservedInputsHashset = emptyConcurrentHashSetOfFileDescriptor;
            }
            else
            {
                ObservedInputsHashset = new ConcurrentHashSet<FileDescriptor>();
            }

            if ((loadOptions & ExecutionLogLoadOptions.LoadBuildGraph) == 0)
            {
                AdjacentInNodesHashset = emptyConcurrentHashSetOfPipDescriptor;
                AdjacentOutNodesHashset = emptyConcurrentHashSetOfPipDescriptor;
            }
            else
            {
                AdjacentInNodesHashset = new ConcurrentHashSet<PipDescriptor>();
                AdjacentOutNodesHashset = new ConcurrentHashSet<PipDescriptor>();
            }

            if ((loadOptions & ExecutionLogLoadOptions.LoadProcessMonitoringData) == 0)
            {
                ReportedProcessesHashset = emptyConcurrentHashSetOfReportedProcesses;
            }
            else
            {
                ReportedProcessesHashset = new ConcurrentHashSet<ProcessInstanceDescriptor>();
            }

            if ((loadOptions & ExecutionLogLoadOptions.DoNotLoadRarelyUsedPipProperties) == 0)
            {
                UntrackedPathsHashset = new AbsolutePathConcurrentHashSet(m_buildGraph.Context.PathTable);
                UntrackedScopesHashset = new AbsolutePathConcurrentHashSet(m_buildGraph.Context.PathTable);
                EnvironmentVariablesDictionary = new StringIdEnvVarDictionary(m_buildGraph.Context);
                foreach (var d in m_fullPip.UntrackedPaths)
                {
                    if (d.IsValid)
                    {
                        UntrackedPathsHashset.Add(d);
                    }
                }

                foreach (var d in m_fullPip.UntrackedScopes)
                {
                    if (d.IsValid)
                    {
                        UntrackedScopesHashset.Add(d);
                    }
                }
            }
            else
            {
                EnvironmentVariablesDictionary = emptyStringIDEnvVarDictionary;
                UntrackedPathsHashset = emptyAbsolutePathConcurrentHashSet;
                UntrackedScopesHashset = emptyAbsolutePathConcurrentHashSet;
            }
        }
        #endregion

        public override string ToString()
        {
            return string.Concat(PipId, ": ", PipName, ", ", ToolName, ", ", QualifierName);
        }
    }
}
