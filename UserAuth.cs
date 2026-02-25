public class UserAuth
{
    public bool CheckUser(string user)
    {
        return !string.IsNullOrWhiteSpace(user);
    }
}