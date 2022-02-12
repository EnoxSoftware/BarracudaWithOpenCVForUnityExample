using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.ObjdetectModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UtilsModule;
using System.Linq;
using Unity.Barracuda;
using UnityEngine;
using UnityEngine.SceneManagement;
using Rect = OpenCVForUnity.CoreModule.Rect;

namespace BarracudaWithOpenCVForUnityExample
{
    /// <summary>
    /// EmotionFerPlus Barracuda OpenCV Example
    /// 
    /// An example of emotional recognition using Unity Barracuda and OpenCV.
    /// EmotionFerPlusBarracudaOpenCVExample is a rewritten version of keijiro's EmotionFerPlus example (https://github.com/keijiro/EmotionFerPlus).
    /// The only difference is that it shows how to use OpenCV for pre-processing and post-processing a tensor data.
    /// Naturally, this way is slower than using compute shader, but it may be useful for quick prototyping.
    /// 
    /// About the ONNX model:
    /// The ONNX file(emotion-ferplus-8.onnx) contained in this repository was obtained from the ONNX model zoo (https://github.com/onnx/models/tree/master/vision/body_analysis/emotion_ferplus).
    /// 
    /// About the haarcascade classifier file:
    /// The haarcascade classifier file (haarcascade_frontalface_alt.xml) contained in this repository was obtained from the opencv repository (https://github.com/opencv/opencv/tree/master/data/haarcascades).
    /// </summary>
    public class EmotionFerPlusBarracudaOpenCVExample : MonoBehaviour
    {
        #region Editable attributes

        [SerializeField] Texture2D _image = null;
        [SerializeField] NNModel _model = null;
        [SerializeField] UnityEngine.UI.RawImage _preview = null;
        [SerializeField] UnityEngine.UI.Text _label = null;

        #endregion

        #region Compile-time constants

        const int ImageSize = 64;

        readonly static string[] Labels =
          { "Neutral", "Happiness", "Surprise", "Sadness",
        "Anger", "Disgust", "Fear", "Contempt"};

        CascadeClassifier cascade;

        protected static readonly string HAAR_CASCADE_FILENAME = "haarcascade_frontalface_alt.xml";

        #endregion

        #region MonoBehaviour implementation

#if UNITY_WEBGL
        IEnumerator getFilePath_Coroutine;
#endif

        // Use this for initialization
        void Start()
        {
#if UNITY_WEBGL
            getFilePath_Coroutine = Utils.getFilePathAsync (HAAR_CASCADE_FILENAME, 
                (result) => {
                    getFilePath_Coroutine = null;

                    if (string.IsNullOrEmpty(result))
                    {
                        Debug.LogError(HAAR_CASCADE_FILENAME + " is not loaded.");
                    }
                    else
                    {
                        cascade = new CascadeClassifier(result);
                    }

                    Run ();
                }, 
                (result, progress) => {
                    Debug.Log ("getFilePathAsync() progress : " + result + " " + Mathf.CeilToInt (progress * 100) + "%");
                });
            StartCoroutine (getFilePath_Coroutine);
#else

            string cascade_filepath = Utils.getFilePath(HAAR_CASCADE_FILENAME);
            if (string.IsNullOrEmpty(cascade_filepath))
            {
                Debug.LogError(HAAR_CASCADE_FILENAME + " is not loaded.");
            }
            else
            {
                cascade = new CascadeClassifier(cascade_filepath);
            }

            Run();
#endif
        }

        /// <summary>
        /// Raises the destroy event.
        /// </summary>
        void OnDestroy()
        {
            if (cascade != null)
                cascade.Dispose();

#if UNITY_WEBGL
            if (getFilePath_Coroutine != null) {
                StopCoroutine (getFilePath_Coroutine);
                ((IDisposable)getFilePath_Coroutine).Dispose ();
            }
#endif
        }

        #endregion

        #region Private members

        private void Run()
        {

            // Convert the Texture2D image to an OpenCV Mat.
            using Mat imageMat = new Mat(_image.height, _image.width, CvType.CV_8UC3);
            Utils.texture2DToMat(_image, imageMat, true);

            using Mat grayscaleMat = new Mat();
            Imgproc.cvtColor(imageMat, grayscaleMat, Imgproc.COLOR_RGB2GRAY);


            // Crop the face area in the image. (Improves recognition accuracy)
            Rect faceRect = DetectFace(grayscaleMat);
            Mat faceMat;
            if (!faceRect.empty())
            {
                // Create ROI.
                faceMat = new Mat(grayscaleMat, faceRect);
            }
            else
            {
                faceMat = grayscaleMat;
            }


            // Preprocessing
            //
            // Convert the image Mat into a 64x64 8UC1 Mat.
            // The model expects input of the shape (Nx1x64x64), where N is the batch size.
            using Mat resizedMat = new Mat();
            Imgproc.resize(faceMat, resizedMat, new Size(ImageSize, ImageSize));
            using Mat inputMat = new Mat();
            resizedMat.convertTo(inputMat, CvType.CV_32FC1);
            //Debug.Log("inputMat:\n" + inputMat.dump());

            // Create a 1x64x64x1 (NHWC) tensor from the input Mat.
            float[] inputArr = new float[ImageSize * ImageSize];
            MatUtils.copyFromMat(inputMat, inputArr);
            using var input = new Tensor(1, ImageSize, ImageSize, 1, inputArr);


            // Run the Emotion FERPlus model.
            using var worker = ModelLoader.Load(_model).CreateWorker();
            worker.Execute(input);

            // Inspect the output tensor.
            // The model outputs a(1x8) array of scores corresponding to the 8 emotion classes, where the labels map as follows: emotion_table = { 'neutral':0, 'happiness':1, 'surprise':2, 'sadness':3, 'anger':4, 'disgust':5, 'fear':6, 'contempt':7}
            var output = worker.PeekOutput();


            // Postprocessing
            //
            // Route the model output through a softmax function to map the aggregated activations across the network to probabilities across the 8 classes.
            using Mat outputMat = new Mat(1, 8, CvType.CV_32FC1);
            MatUtils.copyToMat(output.ToReadOnlyArray(), outputMat);
            using Mat probsMat = SoftMax(outputMat);
            Debug.Log("probsMat: " + probsMat.dump());
            float[] probs = new float[probsMat.total()];
            MatUtils.copyFromMat(probsMat, probs);
            var lines = Labels.Zip(probs, (l, p) => $"{l,-12}: {p:0.00}");


            // Show the results on the UI.
            _label.text = string.Join("\n", lines);

            // Draw the detected face rectangle.
            if (!faceRect.empty())
            {
                Debug.Log("detected face: " + faceRect);
                Imgproc.rectangle(imageMat, new Point(faceRect.x, faceRect.y), new Point(faceRect.x + faceRect.width, faceRect.y + faceRect.height), new Scalar(255, 0, 0, 255), 2);
            }


            Texture2D new_image = new Texture2D(_image.width, _image.height, _image.format, false);
            Utils.matToTexture2D(imageMat, new_image);
            _preview.texture = new_image;


            if (faceMat != null)
                faceMat.Dispose();
        }

        private Rect DetectFace(Mat imageMat)
        {
            if (imageMat.type() != CvType.CV_8UC1)
                return new Rect();

            using MatOfRect faces = new MatOfRect();

            if (cascade != null)
                cascade.detectMultiScale(imageMat, faces, 1.1, 2, 2, // TODO: objdetect.CV_HAAR_SCALE_IMAGE
                    new Size(imageMat.cols() * 0.2, imageMat.rows() * 0.2), new Size());

            if (faces.rows() > 0)
            {
                Rect face = faces.toArray()[0];
                Point center = new Point(face.x + face.width / 2, face.y + face.height / 2);
                int max = Mathf.Max(face.width, face.height);
                return new Rect((int)center.x - max / 2, (int)center.y - max / 2, max, max);
            }
            else
            {
                return new Rect();
            }
        }

        /// <summary>
        /// Implement softmax with OpenCV
        /// </summary>
        /// <param name="src">src</param>
        /// <returns>dst</returns>
        private Mat SoftMax(Mat src)
        {
            Mat dst = src.clone();

            Core.MinMaxLocResult result = Core.minMaxLoc(src);
            Scalar max = new Scalar(result.maxVal);
            Core.subtract(src, max, dst);
            Core.exp(dst, dst);
            Scalar sum = Core.sumElems(dst);
            Core.divide(dst, sum, dst);

            return dst;
        }

        #endregion

        #region Public methods

        public void OnBackButtonClick()
        {
            SceneManager.LoadScene("BarracudaWithOpenCVForUnityExample");
        }

        #endregion
    }
}
