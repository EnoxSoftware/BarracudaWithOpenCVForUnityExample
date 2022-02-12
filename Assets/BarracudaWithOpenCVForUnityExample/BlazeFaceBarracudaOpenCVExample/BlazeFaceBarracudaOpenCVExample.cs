using MediaPipe.BlazeFace;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static MediaPipe.BlazeFace.FaceDetector;
using UI = UnityEngine.UI;

namespace BarracudaWithOpenCVForUnityExample
{
    /// <summary>
    /// BlazeFaceBarracuda OpenCV Example
    /// 
    /// An example of using keijiro's BlazeFaceBarracuda  (https://github.com/keijiro/BlazeFaceBarracuda) to run a lightweight face detector
    /// on the Unity Barracuda neural network inference library, and integrating the results with OpenCV image processing.
    /// </summary>
    [RequireComponent(typeof(WebCamTextureToMatHelper))]
    public class BlazeFaceBarracudaOpenCVExample : MonoBehaviour
    {
        #region Editable attributes

        [SerializeField] Texture2D _image = null;
        [Space]
        [SerializeField, Range(0, 1)] float _threshold = 0.75f;
        [Space]
        [SerializeField] UI.RawImage _previewUI = null;
        [Space]
        [SerializeField] ResourceSet _resources = null;

        #endregion

        #region Compile-time constants

        const int InputTensorSize = 128; // ResourceSet : BlazeFace128 = 128 or BlazeFace256 = 256

        #endregion

        #region MonoBehaviour implementation

        void Start()
        {

            _fpsMonitor = GetComponent<FpsMonitor>();
            _webCamTextureToMatHelper = gameObject.GetComponent<WebCamTextureToMatHelper>();

            _imageRT = new RenderTexture(InputTensorSize, InputTensorSize, 0);

            // Face detector initialization
            _detector = new FaceDetector(_resources);

            // Static image test: Run the detector once.
            if (_image != null)
            {
                Detection[] detections = DetectFace(_image);

                Mat imageMat = new Mat(_image.height, _image.width, CvType.CV_8UC3);
                Utils.texture2DToMat(_image, imageMat);

                foreach (var detection in detections)
                {
                    DrawDetection(imageMat, detection);
                }

                Texture2D new_image = new Texture2D(_image.width, _image.height, _image.format, false);
                Utils.matToTexture2D(imageMat, new_image);

                _previewUI.texture = new_image;
                _previewUI.GetComponent<AspectRatioFitter>().aspectRatio = (float)_image.width / _image.height;
            }
            else
            {
                _webCamTextureToMatHelper.Initialize();
            }
        }

        void OnDestroy()
        {
            _detector?.Dispose();
            _webCamTextureToMatHelper.Dispose();

            Object.Destroy(_imageRT);
        }

        // Update is called once per frame
        void Update()
        {
            if (_webCamTextureToMatHelper.IsPlaying() && _webCamTextureToMatHelper.DidUpdateThisFrame())
            {
                Mat webCamTextureMat = _webCamTextureToMatHelper.GetMat();
                Utils.fastMatToTexture2D(webCamTextureMat, _webCamImageTex, true, 0, true);

                Detection[] detections = DetectFace(_webCamImageTex);

                foreach (var detection in detections)
                {
                    DrawDetection(webCamTextureMat, detection);
                }

                Utils.fastMatToTexture2D(webCamTextureMat, _webCamImageTex);
            }
        }

        #endregion

        #region Private members

        RenderTexture _imageRT;
        FaceDetector _detector;
        WebCamTextureToMatHelper _webCamTextureToMatHelper;
        Texture2D _webCamImageTex;
        FpsMonitor _fpsMonitor;

        Detection[] DetectFace(Texture input)
        {
            // Transforms the input image into a 128x128 or 256x256 tensor while keeping the aspect
            // ratio (what is expected by the corresponding face detection model), resulting
            // in potential letterboxing in the transformed image.
            Matrix4x4 preprocessMatrix = CalculatePreprocessMatrix(input, _imageRT);

            Matrix4x4 inverseMatrix = preprocessMatrix.inverse;
            Graphics.Blit(input, _imageRT, new Vector2(inverseMatrix.m00, inverseMatrix.m11), new Vector2(inverseMatrix.m03, inverseMatrix.m13));

            _detector.ProcessImage(_imageRT, _threshold);

            Detection[] detections = new Detection[_detector.Detections.Count()];

            // Cancel the effect of the preprocessing matrix.
            int i = 0;
            foreach (var detection in _detector.Detections)
            {
                detections[i] = ApplyMatrix(detection, inverseMatrix);
                i++;
            }

            return detections;
        }

        // Calculate the transformation matrix to project the input image to the output image while maintaining the aspect ratio.
        Matrix4x4 CalculatePreprocessMatrix(Texture src, Texture dst)
        {
            float aspect_ratio_src = (float)src.width / src.height;
            float aspect_ratio_dst = (float)dst.width / dst.height;

            float x = 0;
            float y = 0;
            float w = 0;
            float h = 0;
            float translate_x = 0;
            float translate_y = 0;
            float scale_x = 1f;
            float scale_y = 1f;

            if (aspect_ratio_src > aspect_ratio_dst)
            {
                h = dst.width / ((float)src.width / src.height);
                y = (dst.height - h) / 2f;

                translate_y = y / dst.height;
                scale_y = h / dst.height;
            }
            else
            {
                w = dst.height / ((float)src.height / src.width);
                x = (dst.width - w) / 2f;

                translate_x = x / dst.width;
                scale_x = w / dst.width;
            }

            return Matrix4x4.TRS(new Vector3(translate_x, translate_y, 0), Quaternion.identity, new Vector3(scale_x, scale_y, 1));
        }

        Detection ApplyMatrix(Detection detection, Matrix4x4 matrix)
        {
            float[] buf = new float[Detection.Size];

            GCHandle gch = GCHandle.Alloc(buf, GCHandleType.Pinned);
            Marshal.StructureToPtr(detection, gch.AddrOfPinnedObject(), false);
            gch.Free();

            buf[0] = detection.center.x * matrix.m00 + matrix.m03;
            buf[1] = detection.center.y * matrix.m11 + matrix.m13;
            buf[2] = detection.extent.x * matrix.m00;
            buf[3] = detection.extent.y * matrix.m11;
            buf[4] = detection.leftEye.x * matrix.m00 + matrix.m03;
            buf[5] = detection.leftEye.y * matrix.m11 + matrix.m13;
            buf[6] = detection.rightEye.x * matrix.m00 + matrix.m03;
            buf[7] = detection.rightEye.y * matrix.m11 + matrix.m13;
            buf[8] = detection.nose.x * matrix.m00 + matrix.m03;
            buf[9] = detection.nose.y * matrix.m11 + matrix.m13;
            buf[10] = detection.mouth.x * matrix.m00 + matrix.m03;
            buf[11] = detection.mouth.y * matrix.m11 + matrix.m13;
            buf[12] = detection.leftEar.x * matrix.m00 + matrix.m03;
            buf[13] = detection.leftEar.y * matrix.m11 + matrix.m13;
            buf[14] = detection.rightEar.x * matrix.m00 + matrix.m03;
            buf[15] = detection.rightEar.y * matrix.m11 + matrix.m13;

            gch = GCHandle.Alloc(buf, GCHandleType.Pinned);
            Detection new_detection = (Detection)Marshal.PtrToStructure(gch.AddrOfPinnedObject(), typeof(Detection));
            gch.Free();

            return new_detection;
        }

        void DrawDetection(Mat frame, Detection detection)
        {
            var frameSize = new Vector2(frame.width(), frame.height());
            var boxColor = new Scalar(255, 0, 0, 255);
            var pointColor = new Scalar(255, 255, 0, 255);

            // Bounding box center
            var anchoredPosition = new Vector2(detection.center.x, 1f - detection.center.y) * frameSize;

            // Bounding box size
            var size = detection.extent * frameSize;

            var left = anchoredPosition.x - size.x / 2;
            var top = anchoredPosition.y - size.y / 2;
            var right = anchoredPosition.x + size.x / 2;
            var bottom = anchoredPosition.y + size.y / 2;
            Imgproc.rectangle(frame, new Point(left, top), new Point(right, bottom), boxColor, 2);

            // Key points
            var leftEye_p = new Vector2(detection.leftEye.x, 1f - detection.leftEye.y) * frameSize;
            Imgproc.circle(frame, new Point(leftEye_p.x, leftEye_p.y), 5, pointColor, -1);
            var rightEye_p = new Vector2(detection.rightEye.x, 1f - detection.rightEye.y) * frameSize;
            Imgproc.circle(frame, new Point(rightEye_p.x, rightEye_p.y), 5, pointColor, -1);
            var nose_p = new Vector2(detection.nose.x, 1f - detection.nose.y) * frameSize;
            Imgproc.circle(frame, new Point(nose_p.x, nose_p.y), 5, pointColor, -1);
            var mouth_p = new Vector2(detection.mouth.x, 1f - detection.mouth.y) * frameSize;
            Imgproc.circle(frame, new Point(mouth_p.x, mouth_p.y), 5, pointColor, -1);
            var leftEar_p = new Vector2(detection.leftEar.x, 1f - detection.leftEar.y) * frameSize;
            Imgproc.circle(frame, new Point(leftEar_p.x, leftEar_p.y), 5, pointColor, -1);
            var rightEar_p = new Vector2(detection.rightEar.x, 1f - detection.rightEar.y) * frameSize;
            Imgproc.circle(frame, new Point(rightEar_p.x, rightEar_p.y), 5, pointColor, -1);

            // Label
            string label = detection.score.ToString();
            int[] baseLine = new int[1];
            Size labelSize = Imgproc.getTextSize(label, Imgproc.FONT_HERSHEY_SIMPLEX, 0.5, 1, baseLine);

            top = Mathf.Max((float)top, (float)labelSize.height);
            Imgproc.rectangle(frame, new Point(left, top - labelSize.height),
                new Point(left + labelSize.width, top + baseLine[0]), Scalar.all(255), Core.FILLED);
            Imgproc.putText(frame, label, new Point(left, top), Imgproc.FONT_HERSHEY_SIMPLEX, 0.5, new Scalar(0, 0, 0, 255));
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Raises the webcam texture to mat helper initialized event.
        /// </summary>
        public void OnWebCamTextureToMatHelperInitialized()
        {
            Debug.Log("OnWebCamTextureToMatHelperInitialized");

            Mat webCamTextureMat = _webCamTextureToMatHelper.GetMat();

            _webCamImageTex = new Texture2D(webCamTextureMat.cols(), webCamTextureMat.rows(), TextureFormat.RGBA32, false);
            Utils.fastMatToTexture2D(webCamTextureMat, _webCamImageTex);

            _previewUI.texture = _webCamImageTex;
            _previewUI.GetComponent<AspectRatioFitter>().aspectRatio = (float)_webCamImageTex.width / _webCamImageTex.height;
        }

        /// <summary>
        /// Raises the webcam texture to mat helper disposed event.
        /// </summary>
        public void OnWebCamTextureToMatHelperDisposed()
        {
            Debug.Log("OnWebCamTextureToMatHelperDisposed");

            if (_webCamImageTex != null)
            {
                Texture2D.Destroy(_webCamImageTex);
                _webCamImageTex = null;
            }
        }

        /// <summary>
        /// Raises the webcam texture to mat helper error occurred event.
        /// </summary>
        /// <param name="errorCode">Error code.</param>
        public void OnWebCamTextureToMatHelperErrorOccurred(WebCamTextureToMatHelper.ErrorCode errorCode)
        {
            Debug.Log("OnWebCamTextureToMatHelperErrorOccurred " + errorCode);

            if (_fpsMonitor != null)
            {
                _fpsMonitor.consoleText = "ErrorCode: " + errorCode;
            }
        }

        public void OnBackButtonClick()
        {
            SceneManager.LoadScene("BarracudaWithOpenCVForUnityExample");
        }

        /// <summary>
        /// Raises the play button click event.
        /// </summary>
        public void OnPlayButtonClick()
        {
            _webCamTextureToMatHelper.Play();
        }

        /// <summary>
        /// Raises the pause button click event.
        /// </summary>
        public void OnPauseButtonClick()
        {
            _webCamTextureToMatHelper.Pause();
        }

        /// <summary>
        /// Raises the stop button click event.
        /// </summary>
        public void OnStopButtonClick()
        {
            _webCamTextureToMatHelper.Stop();
        }

        /// <summary>
        /// Raises the change camera button click event.
        /// </summary>
        public void OnChangeCameraButtonClick()
        {
            _webCamTextureToMatHelper.requestedIsFrontFacing = !_webCamTextureToMatHelper.requestedIsFrontFacing;
        }

        #endregion

    }
}
