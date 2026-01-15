using System.Security.Cryptography;

namespace PGEmu.gui.ViewModels;

public class LoginViewModel : ViewModelBase
{
    string username = string.Empty;
    public string password = string.Empty;

    public void VerifyLogin()
    {
        // to do

        mainWindowViewModel.SwitchScreens(homeScreenViewModel);
    }

    public void CreateAccount()
    {
        if (CredentialsValid())
        {
            // to do
            
            SwitchScreens(homeScreenViewModel);
            mainWindowViewModel.loggedIn = true;
        }
        ;
    }

    bool CredentialsValid()
    {
        return true;
    }
    
    public void SwitchScreens(ViewModelBase vm)
    {

            mainWindowViewModel.SwitchScreens(vm);
        
        
    }
}