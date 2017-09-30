namespace MSBuildProjectTools.LanguageServer.Documents
{
    /// <summary>
    ///     A kind of <see cref="ProjectDocument"/>.
    /// </summary>
    public enum ProjectDocumentKind
    {
        /// <summary>
        ///     A project (*.*proj).
        /// </summary>
        Project = 1,

        /// <summary>
        ///     A properties file (*.props).
        /// </summary>
        Properties = 2,

        /// <summary>
        ///     A targets file (*.targets).
        /// </summary>
        Targets = 3,

        /// <summary>
        ///     A tasks file (*.tasks).
        /// </summary>
        Tasks = 4,

        /// <summary>
        ///     Some other file type (*.*).
        /// </summary>
        Other  = 5
    }
}
