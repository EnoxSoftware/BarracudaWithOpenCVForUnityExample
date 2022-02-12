using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UtilsModule;
using System.Linq;
using Unity.Barracuda;
using UnityEngine;
using UnityEngine.SceneManagement;
using UI = UnityEngine.UI;

namespace BarracudaWithOpenCVForUnityExample
{
    /// <summary>
    /// Mnist Barracuda OpenCV Example
    /// 
    /// An example of predicting handwritten digits using Unity Barracuda and OpenCV.
    /// MnistBarracudaOpenCVExample is a rewritten version of keijiro's MnistBarracudaGPU example (https://github.com/keijiro/MnistBarracudaGpu).
    /// The only difference is that it shows how to use OpenCV for pre-processing and post-processing a tensor data.
    /// Naturally, this way is slower than using compute shader, but it may be useful for quick prototyping.
    /// 
    /// About the ONNX model:
    /// The ONNX file(mnist-8.onnx) contained in this repository was obtained from the ONNX model zoo (https://github.com/onnx/models/tree/master/vision/classification/mnist).
    /// </summary>
    public class MnistBarracudaOpenCVExample : MonoBehaviour
    {
        public NNModel _model;
        public Texture2D _image;
        public UI.RawImage _imageView;
        public UI.Text _textView;

        // Start is called before the first frame update
        void Start()
        {
            // Convert the Texture2D image to an OpenCV Mat.
            using Mat imageMat = new Mat(_image.height, _image.width, CvType.CV_8UC3);
            Utils.texture2DToMat(_image, imageMat, true);


            // Preprocessing
            //
            // Convert the image Mat into a 28x28 32FC1 Mat.
            // Images are resized into (28x28) in grayscale, with a black background and a white foreground (the number should be in white). Color value is scaled to [0.0, 1.0].
            using Mat grayscaleMat = new Mat();
            Imgproc.cvtColor(imageMat, grayscaleMat, Imgproc.COLOR_RGB2GRAY);
            using Mat resizedMat = new Mat();
            Imgproc.resize(grayscaleMat, resizedMat, new Size(28, 28));
            using Mat inputMat = new Mat();
            resizedMat.convertTo(inputMat, CvType.CV_32FC1, 1 / 255.0);
            //Debug.Log("inputMat:\n" + inputMat.dump());

            // Create a 1x28x28x1 (NHWC) tensor from the input Mat.
            float[] inputArr = new float[28 * 28];
            MatUtils.copyFromMat(inputMat, inputArr);
            using var input = new Tensor(1, 28, 28, 1, inputArr);


            // Run the MNIST model.
            using var worker = ModelLoader.Load(_model).CreateWorker();
            worker.Execute(input);

            // Inspect the output tensor.
            // The likelihood of each number before softmax, with shape of (1x10).
            var output = worker.PeekOutput();


            // Postprocessing
            //
            // Route the model output through a softmax function to map the aggregated activations across the network to probabilities across the 10 classes.
            using Mat outputMat = new Mat(1, 10, CvType.CV_32FC1);
            MatUtils.copyToMat(output.ToReadOnlyArray(), outputMat);
            using Mat scoresMat = SoftMax(outputMat);
            float[] scores = new float[scoresMat.total()];
            MatUtils.copyFromMat(scoresMat, scores);
            //Debug.Log("scoresMat: " + scoresMat.dump());


            // Show the results on the UI.
            _imageView.texture = _image;
            _textView.text = Enumerable.Range(0, 10).
                             Select(i => $"{i}: {scores[i]:0.00}").
                             Aggregate((t, s) => t + "\n" + s);

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

        public void OnBackButtonClick()
        {
            SceneManager.LoadScene("BarracudaWithOpenCVForUnityExample");
        }
    }
}