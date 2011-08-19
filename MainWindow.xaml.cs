/////////////////////////////////////////////////////////////////////////
//
// This module contains code to do Kinect NUI initialization and
// processing and also to display NUI streams on screen.
//
// Copyright ｩ Microsoft Corporation.  All rights reserved.  
// This code is licensed under the terms of the 
// Microsoft Kinect for Windows SDK (Beta) from Microsoft Research 
// License Agreement: http://research.microsoft.com/KinectSDK-ToU
//
/////////////////////////////////////////////////////////////////////////
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Research.Kinect.Nui;

using Microsoft.Research.Kinect.Audio;
using Microsoft.Speech.AudioFormat;
using Microsoft.Speech.Recognition;
using System.Threading;
using System.Collections;

using SkeletalViewer;

namespace SkeletalViewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        enum I4C3DMode {
            ZOOM_IN,
            ZOOM_OUT,
            LEFT,
            RIGHT,
            UP,
            DOWN,
            STOP,
        };
        const int HISTORY_COUNT = 5;
        const int PERSON_COUNT  = 7;    // 3bit

        public MainWindow()
        {
            InitializeComponent();
        }

        static Runtime nui = null;
        int totalFrames = 0;
        int lastFrames = 0;
        DateTime lastTime = DateTime.MaxValue;
        static Thread cameraThread = null;
        static Thread commandThread = null;
        static I4C3D i4c3d;
        static bool i4c3dStarted   = false;
        //static I4C3DMode i4c3dMode = I4C3DMode.STOP;
        static I4C3DMode i4c3dMode = I4C3DMode.LEFT;

        // We want to control how depth data gets converted into false-color data
        // for more intuitive visualization, so we keep 32-bit color frame buffer versions of
        // these, to be updated whenever we receive and process a 16-bit frame.
        const int RED_IDX = 2;
        const int GREEN_IDX = 1;
        const int BLUE_IDX = 0;
        byte[] depthFrame32 = new byte[320 * 240 * 4];
        
        // About Speech
        private const string RecognizerId = "SR_MS_en-US_Kinect_10.0";
        private const string COMMAND_START      = "camera start";
        private const string COMMAND_FIX        = "camera fix";
        private const string COMMAND_DEFAULT    = "default";
        private const string COMMAND_UP         = "up";
        private const string COMMAND_DOWN       = "down";
        //private const string COMMAND_MAXIMUM    = "maximum";
        //private const string COMMAND_MINIMUM    = "minimum";

        private const string COMMAND_INITIALIZE = "kinekuto";
        private const string COMMAND_ZOOM_IN    = "in";
        private const string COMMAND_ZOOM_OUT = "out";
        private const string COMMAND_LEFT = "left";
        private const string COMMAND_RIGHT = "right";
        private const string COMMAND_STOP = "stop";
        //private const string COMMAND_ALIAS      = "alias";
        //private const string COMMAND_MAYA       = "maya";
        //private const string COMMAND_SHOWCASE   = "showcase";
        //private const string COMMAND_RTT        = "rtt";


        static int angle = 0;
        static bool movable = false;
        static bool cameraFixed = false;
        static bool exit = false;
        private static string recognizedText = "...";
        static Hashtable kinectSignTable = null;
        
        Dictionary<JointID,Brush> jointColors = new Dictionary<JointID,Brush>() { 
            {JointID.HipCenter, new SolidColorBrush(Color.FromRgb(169, 176, 155))},
            {JointID.Spine, new SolidColorBrush(Color.FromRgb(169, 176, 155))},
            {JointID.ShoulderCenter, new SolidColorBrush(Color.FromRgb(168, 230, 29))},
            {JointID.Head, new SolidColorBrush(Color.FromRgb(200, 0,   0))},
            {JointID.ShoulderLeft, new SolidColorBrush(Color.FromRgb(79,  84,  33))},
            {JointID.ElbowLeft, new SolidColorBrush(Color.FromRgb(84,  33,  42))},
            {JointID.WristLeft, new SolidColorBrush(Color.FromRgb(255, 126, 0))},
            {JointID.HandLeft, new SolidColorBrush(Color.FromRgb(215,  86, 0))},
            {JointID.ShoulderRight, new SolidColorBrush(Color.FromRgb(33,  79,  84))},
            {JointID.ElbowRight, new SolidColorBrush(Color.FromRgb(33,  33,  84))},
            {JointID.WristRight, new SolidColorBrush(Color.FromRgb(77,  109, 243))},
            {JointID.HandRight, new SolidColorBrush(Color.FromRgb(37,   69, 243))},
            {JointID.HipLeft, new SolidColorBrush(Color.FromRgb(77,  109, 243))},
            {JointID.KneeLeft, new SolidColorBrush(Color.FromRgb(69,  33,  84))},
            {JointID.AnkleLeft, new SolidColorBrush(Color.FromRgb(229, 170, 122))},
            {JointID.FootLeft, new SolidColorBrush(Color.FromRgb(255, 126, 0))},
            {JointID.HipRight, new SolidColorBrush(Color.FromRgb(181, 165, 213))},
            {JointID.KneeRight, new SolidColorBrush(Color.FromRgb(71, 222,  76))},
            {JointID.AnkleRight, new SolidColorBrush(Color.FromRgb(245, 228, 156))},
            {JointID.FootRight, new SolidColorBrush(Color.FromRgb(77,  109, 243))}
        };

        private void Window_Loaded(object sender, EventArgs e)
        {
            nui = new Runtime();

            try
            {
                nui.Initialize(RuntimeOptions.UseDepthAndPlayerIndex | RuntimeOptions.UseSkeletalTracking | RuntimeOptions.UseColor);
            }
            catch (InvalidOperationException)
            {
                System.Windows.MessageBox.Show("Runtime initialization failed. Please make sure Kinect device is plugged in.");
                return;
            }


            try
            {
                nui.VideoStream.Open(ImageStreamType.Video, 2, ImageResolution.Resolution640x480, ImageType.Color);
                nui.DepthStream.Open(ImageStreamType.Depth, 2, ImageResolution.Resolution320x240, ImageType.DepthAndPlayerIndex);
            }
            catch (InvalidOperationException)
            {
                System.Windows.MessageBox.Show("Failed to open stream. Please make sure to specify a supported image type and resolution.");
                return;
            }

            lastTime = DateTime.Now;

            nui.DepthFrameReady += new EventHandler<ImageFrameReadyEventArgs>(nui_DepthFrameReady);
            nui.SkeletonFrameReady += new EventHandler<SkeletonFrameReadyEventArgs>(nui_SkeletonFrameReady);
            nui.VideoFrameReady += new EventHandler<ImageFrameReadyEventArgs>(nui_ColorFrameReady);

            ////////////////////////////////////////////////////////////////////////////////////////
            // About Speech
            kinectSignTable = new Hashtable();
            kinectSignTable.Add(I4C3DMode.ZOOM_IN, "kinect zoomin");
            kinectSignTable.Add(I4C3DMode.ZOOM_OUT, "kinect zoomout");
            kinectSignTable.Add(I4C3DMode.UP, "kinect up");
            kinectSignTable.Add(I4C3DMode.DOWN, "kinect down");
            kinectSignTable.Add(I4C3DMode.LEFT, "kinect left");
            kinectSignTable.Add(I4C3DMode.RIGHT, "kinect right");
            kinectSignTable.Add(I4C3DMode.STOP, "kinect stop");

            // Test!!!!
            cameraThread = new Thread(new ThreadStart(CameraThreadFunc));
            cameraThread.Start();
            commandThread = new Thread(new ThreadStart(CommandThreadFunc));
            commandThread.Start();

            ////////////////////////////////////////////////////////////////////////////////////////

        }

        static void CameraThreadFunc()
        {
            using (var source = new KinectAudioSource())
            {
                //nui.NuiCamera.ElevationAngle = angle;

                source.FeatureMode = true;
                source.AutomaticGainControl = false; //Important to turn this off for speech recognition
                source.SystemMode = SystemMode.OptibeamArrayOnly; //No AEC for this sample

                RecognizerInfo ri = SpeechRecognitionEngine.InstalledRecognizers().Where(r => r.Id == RecognizerId).FirstOrDefault();

                if (ri == null)
                {
                    Console.WriteLine("Could not find speech recognizer: {0}. Please refer to the sample requirements.", RecognizerId);
                    return;
                }

                Console.WriteLine("Using: {0}", ri.Name);

                using (var sre = new SpeechRecognitionEngine(ri.Id))
                {
                    var cameraCommand = new Choices();
                    cameraCommand.Add(COMMAND_START);
                    cameraCommand.Add(COMMAND_FIX);
                    cameraCommand.Add(COMMAND_DEFAULT);
                    cameraCommand.Add(COMMAND_UP);
                    cameraCommand.Add(COMMAND_DOWN);

                    var gb = new GrammarBuilder();
                    //Specify the culture to match the recognizer in case we are running in a different culture.                                 
                    gb.Culture = ri.Culture;
                    gb.Append(cameraCommand);


                    // Create the actual Grammar instance, and then load it into the speech recognizer.
                    var g = new Grammar(gb);

                    sre.LoadGrammar(g);
                    sre.SpeechRecognized += SreSpeechRecognizedCamara;
                    sre.SpeechHypothesized += SreSpeechHypothesized;
                    sre.SpeechRecognitionRejected += SreSpeechRecognitionRejected;

                    using (Stream s = source.Start())
                    {
                        sre.SetInputToAudioStream(s,
                                                  new SpeechAudioFormatInfo(
                                                      EncodingFormat.Pcm, 16000, 16, 1,
                                                      32000, 2, null));

                        Console.WriteLine("Recognizing. Say: 'Start' to start camera move");
                        while (!cameraFixed)
                        {
                            sre.Recognize(new TimeSpan(1000000));   // 1sec
                            //sre.Recognize();
                        }
                        //sre.RecognizeAsync(RecognizeMode.Multiple);
                        Console.WriteLine("Stopping recognizer ...");
                        //sre.RecognizeAsyncStop();
                    }
                }
            }
        }

        static void CommandThreadFunc()
        {
            while (cameraThread != null && !cameraFixed)
            {
                Thread.Sleep(3000);
            }
            if (cameraThread != null)
            {
                cameraThread.Join();
            }

            using (var source = new KinectAudioSource())
            {
                source.FeatureMode = true;
                source.AutomaticGainControl = false; //Important to turn this off for speech recognition
                source.SystemMode = SystemMode.OptibeamArrayOnly; //No AEC for this sample

                RecognizerInfo ri = SpeechRecognitionEngine.InstalledRecognizers().Where(r => r.Id == RecognizerId).FirstOrDefault();

                if (ri == null)
                {
                    Console.WriteLine("Could not find speech recognizer: {0}. Please refer to the sample requirements.", RecognizerId);
                    return;
                }

                Console.WriteLine("Using: {0}", ri.Name);

                using (var sre = new SpeechRecognitionEngine(ri.Id))
                {
                    var i4c3dCommand = new Choices();
                    i4c3dCommand.Add(COMMAND_INITIALIZE);
                    i4c3dCommand.Add(COMMAND_ZOOM_IN);
                    i4c3dCommand.Add(COMMAND_ZOOM_OUT);
                    i4c3dCommand.Add(COMMAND_STOP);
                    i4c3dCommand.Add(COMMAND_LEFT);
                    i4c3dCommand.Add(COMMAND_RIGHT);
                    i4c3dCommand.Add(COMMAND_UP);
                    i4c3dCommand.Add(COMMAND_DOWN);
                    //i4c3dCommand.Add(COMMAND_ALIAS);
                    //i4c3dCommand.Add(COMMAND_MAYA);
                    //i4c3dCommand.Add(COMMAND_RTT);
                    //i4c3dCommand.Add(COMMAND_SHOWCASE);

                    var gb = new GrammarBuilder();
                    //Specify the culture to match the recognizer in case we are running in a different culture.                                 
                    gb.Culture = ri.Culture;
                    gb.Append(i4c3dCommand);


                    // Create the actual Grammar instance, and then load it into the speech recognizer.
                    var g = new Grammar(gb);

                    sre.LoadGrammar(g);
                    sre.SpeechRecognized += SreSpeechRecognizedI4C3D;
                    sre.SpeechHypothesized += SreSpeechHypothesized;
                    sre.SpeechRecognitionRejected += SreSpeechRecognitionRejected;

                    using (Stream s = source.Start())
                    {
                        sre.SetInputToAudioStream(s,
                                                  new SpeechAudioFormatInfo(
                                                      EncodingFormat.Pcm, 16000, 16, 1,
                                                      32000, 2, null));

                        Console.WriteLine("Recognizing. Say: 'Start' to start camera move");
                        
                        while (!i4c3dStarted) Thread.Sleep(1);  // 待機

                        while (!exit)
                        {
                            sre.Recognize(new TimeSpan(100000));   // 1sec
                            //sre.Recognize();
                        }
                        Console.WriteLine("Stopping recognizer ...");
                    }
                }
            }
        }

        // Converts a 16-bit grayscale depth frame which includes player indexes into a 32-bit frame
        // that displays different players in different colors
        byte[] convertDepthFrame(byte[] depthFrame16)
        {
            for (int i16 = 0, i32 = 0; i16 < depthFrame16.Length && i32 < depthFrame32.Length; i16 += 2, i32 += 4)
            {
                int player = depthFrame16[i16] & 0x07;
                int realDepth = (depthFrame16[i16+1] << 5) | (depthFrame16[i16] >> 3);
                // transform 13-bit depth information into an 8-bit intensity appropriate
                // for display (we disregard information in most significant bit)
                byte intensity = (byte)(255 - (255 * realDepth / 0x0fff));

                depthFrame32[i32 + RED_IDX] = 0;
                depthFrame32[i32 + GREEN_IDX] = 0;
                depthFrame32[i32 + BLUE_IDX] = 0;

                // choose different display colors based on player
                switch (player)
                {
                    case 0:
                        depthFrame32[i32 + RED_IDX] = (byte)(intensity / 2);
                        depthFrame32[i32 + GREEN_IDX] = (byte)(intensity / 2);
                        depthFrame32[i32 + BLUE_IDX] = (byte)(intensity / 2);
                        break;
                    case 1:
                        depthFrame32[i32 + RED_IDX] = intensity;
                        break;
                    case 2:
                        depthFrame32[i32 + GREEN_IDX] = intensity;
                        break;
                    case 3:
                        depthFrame32[i32 + RED_IDX] = (byte)(intensity / 4);
                        depthFrame32[i32 + GREEN_IDX] = (byte)(intensity);
                        depthFrame32[i32 + BLUE_IDX] = (byte)(intensity);
                        break;
                    case 4:
                        depthFrame32[i32 + RED_IDX] = (byte)(intensity);
                        depthFrame32[i32 + GREEN_IDX] = (byte)(intensity);
                        depthFrame32[i32 + BLUE_IDX] = (byte)(intensity / 4);
                        break;
                    case 5:
                        depthFrame32[i32 + RED_IDX] = (byte)(intensity);
                        depthFrame32[i32 + GREEN_IDX] = (byte)(intensity / 4);
                        depthFrame32[i32 + BLUE_IDX] = (byte)(intensity);
                        break;
                    case 6:
                        depthFrame32[i32 + RED_IDX] = (byte)(intensity / 2);
                        depthFrame32[i32 + GREEN_IDX] = (byte)(intensity / 2);
                        depthFrame32[i32 + BLUE_IDX] = (byte)(intensity);
                        break;
                    case 7:
                        depthFrame32[i32 + RED_IDX] = (byte)(255 - intensity);
                        depthFrame32[i32 + GREEN_IDX] = (byte)(255 - intensity);
                        depthFrame32[i32 + BLUE_IDX] = (byte)(255 - intensity);
                        break;
                }
            }
            return depthFrame32;
        }

        void nui_DepthFrameReady(object sender, ImageFrameReadyEventArgs e)
        {
            PlanarImage Image = e.ImageFrame.Image;
            byte[] convertedDepthFrame = convertDepthFrame(Image.Bits);

            depth.Source = BitmapSource.Create(
                Image.Width, Image.Height, 96, 96, PixelFormats.Bgr32, null, convertedDepthFrame, Image.Width * 4);

            ++totalFrames;

            DateTime cur = DateTime.Now;
            if (cur.Subtract(lastTime) > TimeSpan.FromSeconds(1))
            {
                int frameDiff = totalFrames - lastFrames;
                lastFrames = totalFrames;
                lastTime = cur;
                frameRate.Text = frameDiff.ToString() + " fps";
                speechText.Text = recognizedText;
            }
        }

        private Point getDisplayPosition(Joint joint)
        {
            float depthX, depthY;
            nui.SkeletonEngine.SkeletonToDepthImage(joint.Position, out depthX, out depthY);
            depthX = Math.Max(0, Math.Min(depthX * 320, 320));  //convert to 320, 240 space
            depthY = Math.Max(0, Math.Min(depthY * 240, 240));  //convert to 320, 240 space
            int colorX, colorY;
            ImageViewArea iv = new ImageViewArea();
            // only ImageResolution.Resolution640x480 is supported at this point
            nui.NuiCamera.GetColorPixelCoordinatesFromDepthPixel(ImageResolution.Resolution640x480, iv, (int)depthX, (int)depthY, (short)0, out colorX, out colorY);

            // map back to skeleton.Width & skeleton.Height
            return new Point((int)(skeleton.Width * colorX / 640.0), (int)(skeleton.Height * colorY / 480));
        }

        Polyline getBodySegment(Microsoft.Research.Kinect.Nui.JointsCollection joints, Brush brush, params JointID[] ids)
        {
            PointCollection points = new PointCollection(ids.Length);
            for (int i = 0; i < ids.Length; ++i )
            {
                points.Add(getDisplayPosition(joints[ids[i]]));
            }

            Polyline polyline = new Polyline();
            polyline.Points = points;
            polyline.Stroke = brush;
            polyline.StrokeThickness = 5;
            return polyline;
        }


        int[] dollyHistoryCount = new int[PERSON_COUNT];
        int[] tumbleHistoryCount = new int[PERSON_COUNT];
        float[,] depthZ = new float[PERSON_COUNT, HISTORY_COUNT];
        
        int[,] horizontalHistory    = new int[PERSON_COUNT, 3];
        int[,] verticalHistory      = new int[PERSON_COUNT, 3];

        void nui_SkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            SkeletonFrame skeletonFrame = e.SkeletonFrame;
            int iSkeleton = 0;
            Brush[] brushes = new Brush[6];
            brushes[0] = new SolidColorBrush(Color.FromRgb(255, 0, 0));
            brushes[1] = new SolidColorBrush(Color.FromRgb(0, 255, 0));
            brushes[2] = new SolidColorBrush(Color.FromRgb(64, 255, 255));
            brushes[3] = new SolidColorBrush(Color.FromRgb(255, 255, 64));
            brushes[4] = new SolidColorBrush(Color.FromRgb(255, 64, 255));
            brushes[5] = new SolidColorBrush(Color.FromRgb(128, 128, 255));

            skeleton.Children.Clear();
            foreach (SkeletonData data in skeletonFrame.Skeletons)
            {
                if (SkeletonTrackingState.Tracked == data.TrackingState)
                {
                    // Draw bones
                    Brush brush = brushes[iSkeleton % brushes.Length];
                    skeleton.Children.Add(getBodySegment(data.Joints, brush, JointID.HipCenter, JointID.Spine, JointID.ShoulderCenter, JointID.Head));
                    skeleton.Children.Add(getBodySegment(data.Joints, brush, JointID.ShoulderCenter, JointID.ShoulderLeft, JointID.ElbowLeft, JointID.WristLeft, JointID.HandLeft));
                    skeleton.Children.Add(getBodySegment(data.Joints, brush, JointID.ShoulderCenter, JointID.ShoulderRight, JointID.ElbowRight, JointID.WristRight, JointID.HandRight));
                    skeleton.Children.Add(getBodySegment(data.Joints, brush, JointID.HipCenter, JointID.HipLeft, JointID.KneeLeft, JointID.AnkleLeft, JointID.FootLeft));
                    skeleton.Children.Add(getBodySegment(data.Joints, brush, JointID.HipCenter, JointID.HipRight, JointID.KneeRight, JointID.AnkleRight, JointID.FootRight));

                    // Draw joints
                    foreach (Joint joint in data.Joints)
                    {
                        Point jointPos = getDisplayPosition(joint);

                        // I4C3D //////////////////////////////////////////////////////////////////////////////////
                        if (i4c3dStarted && joint.ID == JointID.HandRight)
                        {
                            if (i4c3dMode == I4C3DMode.ZOOM_IN || i4c3dMode == I4C3DMode.ZOOM_OUT)
                            {
                                depthZ[iSkeleton, dollyHistoryCount[iSkeleton]] = joint.Position.Z;
                                dollyHistoryCount[iSkeleton]++;

                                if (dollyHistoryCount[iSkeleton] == HISTORY_COUNT)
                                {
                                    float diff = depthZ[iSkeleton, HISTORY_COUNT - 1] - depthZ[iSkeleton, 0];
                                    if (Math.Abs(diff) > 0.05)
                                    {
                                        // diffが1以下なので、1以上に変換して送る
                                        string sendMessage;
                                        if (i4c3dMode == I4C3DMode.ZOOM_IN && diff < 0)
                                        {
                                            sendMessage = string.Format("DOLLY {0} {0}?", -diff * 100);
                                            Console.WriteLine(sendMessage);
                                            i4c3d.SendCommandTCP(sendMessage);
                                        }
                                        else if (i4c3dMode == I4C3DMode.ZOOM_OUT && diff > 0)
                                        {
                                            sendMessage = string.Format("DOLLY {0} {0}?", -diff * 100);
                                            Console.WriteLine(sendMessage);
                                            i4c3d.SendCommandTCP(sendMessage);
                                        }
                                    }
                                    dollyHistoryCount[iSkeleton] = 0;
                                }
                            }
                            else if (i4c3dMode != I4C3DMode.STOP)
                            {
                                horizontalHistory[iSkeleton, tumbleHistoryCount[iSkeleton]] = (int)jointPos.X;
                                verticalHistory[iSkeleton, tumbleHistoryCount[iSkeleton]]   = (int)jointPos.Y;

                                tumbleHistoryCount[iSkeleton]++;
                                if (tumbleHistoryCount[iSkeleton] == 3)
                                {
                                    int diff = 0;
                                    string sendMessage = "";
                                    switch (i4c3dMode)
                                        {
                                        case I4C3DMode.LEFT:
                                            diff = horizontalHistory[iSkeleton, 2] - horizontalHistory[iSkeleton, 0];
                                            //sendMessage = "TUMBLE -14.0 0.0?";
                                            sendMessage = string.Format("TUMBLE {0} 0.0?", -diff * 2);
                                            break;
                                        case I4C3DMode.RIGHT:
                                            diff = horizontalHistory[iSkeleton, 0] - horizontalHistory[iSkeleton, 2];
                                            //sendMessage = "TUMBLE 14.0 0.0?";
                                            sendMessage = string.Format("TUMBLE {0} 0.0?", diff * 2);
                                            break;
                                        case I4C3DMode.UP:
                                            diff = verticalHistory[iSkeleton, 0] - verticalHistory[iSkeleton, 2];
                                            //sendMessage = "TUMBLE 0.0 -14.0?";
                                            sendMessage = string.Format("TUMBLE 0.0 {0}?", -diff * 2);
                                            break;
                                        case I4C3DMode.DOWN:
                                            diff = verticalHistory[iSkeleton, 2] - verticalHistory[iSkeleton, 0];
                                            //sendMessage = "TUMBLE 0.0 14.0?";
                                            sendMessage = string.Format("TUMBLE 0.0 {0}?", diff * 2);
                                            break;
                                        }

                                    // ----------> デバッグメッセージ
                                    if (i4c3dMode == I4C3DMode.LEFT || i4c3dMode == I4C3DMode.RIGHT)
                                    {
                                        Console.WriteLine("Horizontal: {0}", diff);
                                    }
                                    else
                                    {
                                        Console.WriteLine("Vertical: {0}", diff);
                                    }
                                    // ----------> デバッグメッセージ

                                    if (diff > 7) {
                                        for (int i = 0; i < 5; i++)
                                        {
                                            i4c3d.SendCommandTCP(sendMessage);
                                            Thread.Sleep(1);
                                        }
                                    }
                                    tumbleHistoryCount[iSkeleton] = 0;
                                }
                            }

                        }
                        //if (joint.ID == JointID.HipCenter)
                        //{
                        //    Console.WriteLine("Hip: {0}", joint.Position.Y);
                        //}

                        // I4C3D //////////////////////////////////////////////////////////////////////////////////

                        Line jointLine = new Line();
                        jointLine.X1 = jointPos.X - 3;
                        jointLine.X2 = jointLine.X1 + 6;
                        jointLine.Y1 = jointLine.Y2 = jointPos.Y;
                        jointLine.Stroke = jointColors[joint.ID];
                        jointLine.StrokeThickness = 6;
                        skeleton.Children.Add(jointLine);
                    }
                }

                iSkeleton++;
            } // for each skeleton
        }

        void nui_ColorFrameReady(object sender, ImageFrameReadyEventArgs e)
        {
            // 32-bit per pixel, RGBA image
            PlanarImage Image = e.ImageFrame.Image;
            video.Source = BitmapSource.Create(
                Image.Width, Image.Height, 96, 96, PixelFormats.Bgr32, null, Image.Bits, Image.Width * Image.BytesPerPixel);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            cameraFixed = true;
            exit = true;
            if (cameraThread != null) {
                cameraThread.Join();
            }
            if (commandThread != null)
            {
                commandThread.Join();
            }
            if (nui != null) {
                nui.Uninitialize();
            }
            Environment.Exit(0);
        }

        //////////////////////////////////////////////////////////////////////////////////////
        // About Speech
        static void SreSpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            Console.WriteLine("\nSpeech Rejected");
            if (e.Result != null)
                DumpRecordedAudio(e.Result.Audio);
        }

        static void SreSpeechHypothesized(object sender, SpeechHypothesizedEventArgs e)
        {
            Console.Write("\rSpeech Hypothesized: \t{0}", e.Result.Text);
            recognizedText = "...";
        }

        static void SreSpeechRecognizedCamara(object sender, SpeechRecognizedEventArgs e)
        {
            //This first release of the Kinect language pack doesn't have a reliable confidence model, so 
            //we don't use e.Result.Confidence here.
            Console.WriteLine("\nSpeech Recognized: \t{0}", e.Result.Text);
            recognizedText = e.Result.Text;

            if (!movable)
            {
                if (e.Result.Text.Equals(COMMAND_START))
                {
                    movable = true;
                }
                else if (e.Result.Text.Equals(COMMAND_FIX))
                {
                    cameraFixed = true;
                }
            }
            else
            {
                if (e.Result.Text.Equals(COMMAND_FIX))
                {
                    movable = false;
                    cameraFixed = true;
                }
                else if (e.Result.Text.Equals(COMMAND_UP))
                {
                    if ((angle += 3) <= Camera.ElevationMaximum)
                    {
                        nui.NuiCamera.ElevationAngle = angle;
                    }
                }
                else if (e.Result.Text.Equals(COMMAND_DOWN))
                {
                    if (Camera.ElevationMinimum <= (angle -= 3))
                    {
                        nui.NuiCamera.ElevationAngle = angle;
                    }
                }
                else if (e.Result.Text.Equals(COMMAND_DEFAULT))
                {
                    angle = 0;
                    nui.NuiCamera.ElevationAngle = angle;
                }
                //else if (e.Result.Text.Equals(COMMAND_MAXIMUM))
                //{
                //    angle = Camera.ElevationMaximum;
                //    nui.NuiCamera.ElevationAngle = angle;
                //}
                //else if (e.Result.Text.Equals(COMMAND_MINIMUM))
                //{
                //    angle = Camera.ElevationMinimum;
                //    nui.NuiCamera.ElevationAngle = angle;
                //}
            }
        }

        static bool isCommandSet = false;
        static void SreSpeechRecognizedI4C3D(object sender, SpeechRecognizedEventArgs e)
        {
            //This first release of the Kinect language pack doesn't have a reliable confidence model, so 
            //we don't use e.Result.Confidence here.
            Console.WriteLine("\nSpeech Recognized: \t{0}", e.Result.Text);
            recognizedText = e.Result.Text;

            if (e.Result.Text.Equals(COMMAND_INITIALIZE))
            {
                isCommandSet = true;
            }
            //if (isCommandSet)
            //{
                if (e.Result.Text.Equals(COMMAND_ZOOM_IN))
                {
                    i4c3dMode = I4C3DMode.ZOOM_IN;
                    isCommandSet = false;
                }
                else if (e.Result.Text.Equals(COMMAND_ZOOM_OUT))
                {
                    i4c3dMode = I4C3DMode.ZOOM_OUT;
                    isCommandSet = false;
                }
                else if (e.Result.Text.Equals(COMMAND_UP))
                {
                    i4c3dMode = I4C3DMode.UP;
                    isCommandSet = false;
                }
                else if (e.Result.Text.Equals(COMMAND_DOWN))
                {
                    i4c3dMode = I4C3DMode.DOWN;
                    isCommandSet = false;
                }
                else if (e.Result.Text.Equals(COMMAND_LEFT))
                {
                    i4c3dMode = I4C3DMode.LEFT;
                    isCommandSet = false;
                }
                else if (e.Result.Text.Equals(COMMAND_RIGHT))
                {
                    i4c3dMode = I4C3DMode.RIGHT;
                    isCommandSet = false;
                }
                //else if (e.Result.Text.Equals(COMMAND_ALIAS))
                //{
                //}
                //else if (e.Result.Text.Equals(COMMAND_MAYA))
                //{
                //}
                //else if (e.Result.Text.Equals(COMMAND_RTT))
                //{
                //}
                //else if (e.Result.Text.Equals(COMMAND_SHOWCASE))
                //{
                //}
            //}

            // StopはいつでもOKとする
            if (e.Result.Text.Equals(COMMAND_STOP))
            {
                i4c3dMode = I4C3DMode.STOP;
                isCommandSet = false;
            }

            Console.WriteLine(kinectSignTable[i4c3dMode]);
            i4c3d.SendCommandUDP((string)kinectSignTable[i4c3dMode]);
        }

        private static void DumpRecordedAudio(RecognizedAudio audio)
        {
            if (audio == null) return;

            int fileId = 0;
            string filename;
            while (File.Exists((filename = "RetainedAudio_" + fileId + ".wav")))
                fileId++;

            Console.WriteLine("\nWriting file: {0}", filename);
            using (var file = new FileStream(filename, System.IO.FileMode.CreateNew))
                audio.WriteToWaveStream(file);
        }

        private void connectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!i4c3dStarted)
            {
                Console.WriteLine("Address:{0} Port1:{1} Port2:{2}", textIPAddress.Text, Convert.ToUInt16(textPortNoCommand.Text), Convert.ToUInt16(textPortNoKinectSign.Text));
                i4c3d = new I4C3D(textIPAddress.Text, Convert.ToUInt16(textPortNoCommand.Text), Convert.ToUInt16(textPortNoKinectSign.Text));
                i4c3dStarted = true;
            }
        }

    }
}
