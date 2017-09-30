using Newtonsoft.Json;
using Nito.AsyncEx;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Serilog.Context;

namespace MSBuildProjectTools.LanguageServer.SemanticModel
{
    /// <summary>
    ///     Caches metadata for MSBuild task assemblies.
    /// </summary>
    public sealed class MSBuildTaskMetadataCache
    {
        /// <summary>
        ///     Settings for serialisation of cache state.
        /// </summary>
        static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            DateFormatHandling = DateFormatHandling.IsoDateFormat
        };

        /// <summary>
        ///     Create a new <see cref="MSBuildTaskMetadataCache"/>.
        /// </summary>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        public MSBuildTaskMetadataCache(ILogger logger)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));
            
            Log = logger.ForContext<MSBuildTaskMetadataCache>();
        }

        /// <summary>
        ///     The cache logger.
        /// </summary>
        [JsonIgnore]
        ILogger Log { get; }

        /// <summary>
        ///     A lock used to synchronise access to cache state.
        /// </summary>
        [JsonIgnore]
        public AsyncLock StateLock { get; } = new AsyncLock();

        /// <summary>
        ///     Does the cache contain entries that have not been persisted?
        /// </summary>
        [JsonIgnore]
        public bool IsDirty { get; set; }

        /// <summary>
        ///     Metadata for assemblies, keyed by the assembly's full path.
        /// </summary>
        [JsonProperty("assemblies", ObjectCreationHandling = ObjectCreationHandling.Reuse)]
        public Dictionary<string, MSBuildTaskAssemblyMetadata> Assemblies = new Dictionary<string, MSBuildTaskAssemblyMetadata>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        ///     Get metadata for the specified task assembly, updating the cache if required.
        /// </summary>
        /// <param name="assemblyPath">
        ///     The full path to the assembly.
        /// </param>
        /// <returns>
        ///     The assembly metadata.
        /// </returns>
        public async Task<MSBuildTaskAssemblyMetadata> GetAssemblyMetadata(string assemblyPath)
        {
            if (String.IsNullOrWhiteSpace(assemblyPath))
                throw new ArgumentException("Argument cannot be null, empty, or entirely composed of whitespace: 'assemblyPath'.", nameof(assemblyPath));

            FileInfo assemblyFile = new FileInfo(assemblyPath);

            using (LogContext.PushProperty("AssemblyPath", assemblyFile.FullName))
            using (Log.BeginTimedOperation("Get metadata for MSBuild task assembly", level: LogEventLevel.Verbose))
            {
                Log.Debug("Retrieving metadata for MSBuild task assembly {AssemblyPath}...", assemblyFile.FullName);

                using (await StateLock.LockAsync())
                {
                    MSBuildTaskAssemblyMetadata metadata;
                    if (!Assemblies.TryGetValue(assemblyFile.FullName, out metadata) || metadata.TimestampUtc < assemblyFile.LastWriteTimeUtc)
                    {
                        Log.Debug("Metadata cache does not have an up-to-date entry for MSBuild task assembly {AssemblyPath}; scanning...", assemblyFile.FullName);
                        
                        metadata = await MSBuildTaskScanner.GetAssemblyTaskMetadata(assemblyPath);
                        Assemblies[assemblyFile.FullName] = metadata;
                        
                        Log.Debug("Scanned MSBuild task assembly {AssemblyPath} for metadata and added it to the cache.", assemblyFile.FullName);

                        IsDirty = true;
                    }
                    else
                        Log.Debug("Metadata cache already contains an up-to-date entry for for MSBuild task assembly {AssemblyPath}.", assemblyFile.FullName);

                    return metadata;
                }
            }
        }

        /// <summary>
        ///     Flush the cache.
        /// </summary>
        public void Flush()
        {
            Log.Debug("Flushing task metadata cache...");

            using (StateLock.Lock())
            {
                Assemblies.Clear();

                IsDirty = false;
            }

            Log.Debug("MSBuild task metadata cache has been flushed.");
        }

        /// <summary>
        ///     Load cache state from the specified file.
        /// </summary>
        /// <param name="cacheFile">
        ///     The file containing persisted cache state.
        /// </param>
        public void Load(string cacheFile)
        {
            if (String.IsNullOrWhiteSpace(cacheFile))
                throw new ArgumentException("Argument cannot be null, empty, or entirely composed of whitespace: 'cacheFile'.", nameof(cacheFile));

            Log.Debug("Loading task metadata cache state from {CacheFile}...", cacheFile);

            using (StateLock.Lock())
            {
                Assemblies.Clear();

                using (StreamReader input = File.OpenText(cacheFile))
                using (JsonTextReader json = new JsonTextReader(input))
                {
                    JsonSerializer.Create(SerializerSettings).Populate(json, this);
                }

                IsDirty = false;
            }

            Log.Debug("Loaded task metadata cache state from {CacheFile}.", cacheFile);
        }

        /// <summary>
        ///     Write cache state to the specified file.
        /// </summary>
        /// <param name="cacheFile">
        ///     The file that will contain cache state.
        /// </param>
        public void Save(string cacheFile)
        {
            if (String.IsNullOrWhiteSpace(cacheFile))
                throw new ArgumentException("Argument cannot be null, empty, or entirely composed of whitespace: 'cacheFile'.", nameof(cacheFile));

            Log.Debug("Persisting task metadata cache state to {CacheFile}...", cacheFile);

            using (StateLock.Lock())
            {
                if (!IsDirty)
                {
                    Log.Debug("Task metadata cache is not dirty; nothing to persist.");

                    return;
                }

                if (File.Exists(cacheFile))
                    File.Delete(cacheFile);

                string cacheDirectory = Path.GetDirectoryName(cacheFile);
                if (!Directory.Exists(cacheDirectory))
                    Directory.CreateDirectory(cacheDirectory);

                using (StreamWriter output = File.CreateText(cacheFile))
                using (JsonTextWriter json = new JsonTextWriter(output))
                {
                    JsonSerializer.Create(SerializerSettings).Serialize(json, this);
                }

                IsDirty = false;
            }

            Log.Debug("Persisted task metadata cache state to {CacheFile}.", cacheFile);
        }        

        /// <summary>
        ///     Create a <see cref="MSBuildTaskMetadataCache"/> using the state persisted in the specified file.
        /// </summary>
        /// <param name="cacheFile">
        ///     The file containing persisted cache state.
        /// </param>
        /// <param name="logger">
        ///     The application logger.
        /// </param>
        /// <returns>
        ///     The new <see cref="MSBuildTaskMetadataCache"/>.
        /// </returns>
        public static MSBuildTaskMetadataCache FromCacheFile(string cacheFile, ILogger logger)
        {
            if (String.IsNullOrWhiteSpace(cacheFile))
                throw new ArgumentException("Argument cannot be null, empty, or entirely composed of whitespace: 'cacheFile'.", nameof(cacheFile));
            
            MSBuildTaskMetadataCache cache = new MSBuildTaskMetadataCache(logger);
            cache.Load(cacheFile);
            
            return cache;
        }
    }
}
