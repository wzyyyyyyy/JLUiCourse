namespace iCourse.Messages
{
    class CloseWindowMessage(Type viewModelType)
    {
        public Type ViewModelType { get; } = viewModelType;
    }
}
