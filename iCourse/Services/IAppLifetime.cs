namespace iCourse.Services;

public interface IAppLifetime
{
    void Shutdown();
    void Restart();
}
