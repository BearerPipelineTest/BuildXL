﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Rush.ProjectGraph
{
    /// <summary>
    /// Generic version of a Rush graph where the project is parametric
    /// </summary>
    /// <remarks>
    /// Useful for deserialization, when the project is a flattened version of what the final project will become
    /// </remarks>
    public class GenericRushGraph<TProject>
    {
        /// <nodoc/>
        public GenericRushGraph(IReadOnlyCollection<TProject> projects, RushConfiguration configuration)
        {
            Projects = projects;
            Configuration = configuration;
        }

        /// <nodoc/>
        public RushConfiguration Configuration { get; }

        /// <nodoc/>
        public IReadOnlyCollection<TProject> Projects { get; }
    }
}
