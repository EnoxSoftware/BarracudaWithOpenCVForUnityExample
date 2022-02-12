using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarracudaWithOpenCVForUnityExample
{

    public class ShowLicense : MonoBehaviour
    {

        public void OnBackButtonClick()
        {
            SceneManager.LoadScene("BarracudaWithOpenCVForUnityExample");
        }
    }
}
