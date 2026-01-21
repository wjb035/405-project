using System.Runtime.InteropServices.Swift;

namespace PGEmu.gui.ViewModels;

public class UserSettingsViewModel : ViewModelBase
{
    private string newPassword;
    private string newPasswordConfirm;
    private string newUserName;
    
    void ChangePassword()
    {
        
        if (PasswordValid(newPassword, newPasswordConfirm))
        {
            
        }
    }

    bool PasswordValid(string newPassword, string newPasswordConfirm)
    {
        return true;
    }


    void ChangeUsername()
    {
        if (UsernameValid(newUserName))
        {
            //
        }
        
    }

    bool UsernameValid(string newUserName)
    {
        return true;
    }


    public void GoHome()
    {
        SwitchScreens(homeScreenViewModel);
    }
    public void SwitchScreens(ViewModelBase vm)
    {

        mainWindowViewModel.SwitchScreens(vm);
        
        
    }
}