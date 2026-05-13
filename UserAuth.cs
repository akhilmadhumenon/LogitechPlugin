public class UserAuth
{
    public bool CheckUser(string user)
    {
        if (user != null)
        {
            if (user.Length > 0)
            {
                return true;
            }
        }
        return false;
    }
}