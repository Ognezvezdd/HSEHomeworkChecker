namespace PublicApi
{
    // Описывает форму, которую отправляет студент при загрузке работы.
    public class UploadWorkRequest
    {
        public IFormFile File { get; set; } = null!;

        public string StudentId { get; set; } = string.Empty;

        public string StudentName { get; set; } = string.Empty;

        public string AssignmentId { get; set; } = string.Empty;
    }
}