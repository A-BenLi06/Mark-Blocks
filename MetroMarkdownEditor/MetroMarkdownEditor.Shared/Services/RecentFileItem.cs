namespace MetroMarkdownEditor.Services
{
    public class RecentFileItem
    {
        public string Token { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        
        /// <summary>
        /// Indicates this is a temporary unsaved file
        /// </summary>
        public bool IsTemporary { get; set; }
        
        /// <summary>
        /// Content for temporary unsaved files
        /// </summary>
        public string TempContent { get; set; }
    }
}
