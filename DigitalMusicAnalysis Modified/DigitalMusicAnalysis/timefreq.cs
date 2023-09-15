using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Tasks;

using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.IO;
using static System.Math;

namespace DigitalMusicAnalysis
{
    public class timefreq
    {
        public float[][] timeFreqData;
        public int wSamp;
        public Complex[] twiddles;

        #region attempt 2 - Queued FFT
        public async Task timefreq_init(float[] x, int windowSamp)//-- this was the constructor
        {
            Stopwatch stp = new Stopwatch();
            stp.Start();
            this.wSamp = windowSamp;
            twiddles = new Complex[wSamp];
            for (int ii = 0; ii < wSamp; ii++)
                twiddles[ii] = Complex.Pow(Complex.Exp(-Complex.ImaginaryOne), 2 * Math.PI * ii / (double)wSamp);

            generateLookup(wSamp); //-- pre-generate Sample / 2 table

            timeFreqData = new float[wSamp / 2][];

            int nearest = (int)Math.Ceiling((double)x.Length / (double)wSamp);
            nearest = nearest * wSamp;

            Complex[] compX = new Complex[nearest]; //-- complex's are instanced == Complex.zero
            for (int kk = 0; kk < x.Length; kk++)
                compX[kk] = x[kk];
            


            int cols = 2 * nearest / wSamp;

            for (int jj = 0; jj < wSamp / 2; jj++)
            {
                timeFreqData[jj] = new float[cols];
            }

            timeFreqData = await stft(compX, wSamp);
            stp.Stop();
            stpBench.addtime("timefreq", stp.Elapsed);
        }

        async Task<float[][]> stft(Complex[] x, int wSamp, uint targetTask = 10)
        {
            Stopwatch stp = new Stopwatch();
            stp.Start();
            int ii = 0;
            int jj = 0;
            int kk = 0;
            int ll = 0;
            int N = x.Length;
            float fftMax = 0;

            float[][] Y = new float[wSamp / 2][];

            for (ll = 0; ll < wSamp / 2; ll++)
                Y[ll] = new float[2 * (int)Math.Floor((double)N / (double)wSamp)];

            
            ConcurrentQueue<segment> segments = new ConcurrentQueue<segment>();
            segment data = new segment();
            for (ii = 0; ii < 2 * Math.Floor((double)N / (double)wSamp) - 1; ii++)
            {
                data.index = segments.Count; //-- They go in, in order. They may not come back from their threads in order tho, so this lets us keep track
                data.comps = new Complex[wSamp];
                for (jj = 0; jj < wSamp; jj++)
                {
                    data.comps[jj] = x[ii * (wSamp / 2) + jj];
                }
                segments.Enqueue(data); 
            }

            Complex[][] outs = new Complex[segments.Count][];
            int TargetTasks = 8;
            List<Task> procs = new List<Task>(TargetTasks);
            int PresentTasks = TargetTasks;
            while (PresentTasks > 0)
            {
                if (segments.Count <= TargetTasks)
                    break;

                procs.Add(Task.Run(() => selfmaint_radfft(ref segments, ref outs)));
                PresentTasks--;
            }

            await Task.WhenAll(procs);
            stpBench.addtime("task completion", stp.Elapsed);


            for (int o = 0; o < 2 * Math.Floor((double)N / (double)wSamp) - 1; o++) //~60
                for (kk = 0; kk < wSamp / 2; kk++)
                {
                    float result = (float)Complex.Abs(outs[o][kk]);
                    Y[kk][o] = result;
                    if (result > fftMax)
                        fftMax = result;
                }


            for (int o = 0; o < 2 * Math.Floor((double)N / (double)wSamp) - 1; o++) //~50
                for (kk = 0; kk < wSamp / 2; kk++)
                    Y[kk][o] /= fftMax;

            stp.Stop();
            stpBench.addtime("stft", stp.Elapsed);

            return Y;
        }


        struct segment
        {
            public Complex[] comps;
            public int index;
        }


        #region Recursive FFT
        Task selfmaint_fft(ref ConcurrentQueue<segment> comps, ref Complex[][] outs)
        {
            segment current;
            while (comps.Count > 0)
            {
                //lock (comps)
                //    current = comps.Dequeue();
                if (comps.TryDequeue(out current)) {
                    Complex[] r = fft(current.comps);
                    lock (outs)
                        outs[current.index] = r;
                }
            }

            return Task.CompletedTask;
        }

        Complex[] fft(Complex[] x)
        {
            int ii = 0;
            int kk = 0;
            int N = x.Length;

            Complex[] Y = new Complex[N];

            // NEED TO MEMSET TO ZERO?

            if (N == 1)
            {
                Y[0] = x[0];
                return Y;
            }

            Complex[] even, odd;
            even = new Complex[N / 2];
            odd = new Complex[N / 2];

            for (ii = 0; ii < N; ii++)
            {

                if (ii % 2 == 0)
                {
                    even[ii / 2] = x[ii];
                }
                if (ii % 2 == 1)
                {
                    odd[(ii - 1) / 2] = x[ii];
                }
            }


            //-- THIS IS RECURSIVE AND IS:
            //-- A: ITS FAST DIAGNOSTICALLY BUT SHIT OTHERWISE
            even = fft(even);
            odd = fft(odd);

            for (kk = 0; kk < N; kk++)
            {
                Y[kk] = even[(kk % (N / 2))] + odd[(kk % (N / 2))] * twiddles[kk * wSamp / N];
            }

            return Y;
        }
        #endregion

        #region Radix-2 In-Place
        object threadLock = new object();
        Task selfmaint_radfft(ref ConcurrentQueue<segment> comps, ref Complex[][] outs)
        {
            segment current;
            Complex[] r;
            while (comps.Count > 0)
            {
                //lock (comps)
                //    current = comps.Dequeue();
                if (comps.TryDequeue(out current)) {
                    r = InPlace_bitRev(Radix_DIF_FFT_Iter(current.comps));
                    lock (threadLock)
                        outs[current.index] = r;
                }
            }

            return Task.CompletedTask;
        }

        public Complex[] Radix_DIF_FFT_Iter(Complex[] x)
        {
            int n = x.Length;

            int stepSize = 1;
            int bottomCursor, topCursor, butterflySize, stages;


            for (stages = (int)Math.Log2(n); stages >= 1; stages--)//-- DIF = 8 -> 4 -> 2
            {
                butterflySize = (int)Math.Pow(2, stages);//-- Number of units in the current multiplication
                int twiddleIndex = 0;
                for (int group = 0; group < n / butterflySize; group++)
                {
                    for (int index = 0; index < butterflySize / 2; index++)
                    {
                        //DIT
                        // - Multiply by twiddle before the operation

                        //DIF
                        // - Multiply by twiddle after the operation
                        topCursor = (group * butterflySize) + index;
                        bottomCursor = topCursor + butterflySize / 2;

                        Complex TopLane = x[topCursor];
                        Complex BaseLane = x[bottomCursor];

                        x[topCursor] = TopLane + BaseLane;
                        x[bottomCursor] = (TopLane - BaseLane) * twiddles[twiddleIndex];

                        twiddleIndex = (twiddleIndex + stepSize) % twiddles.Length;
                    }
                }
                stepSize <<= 1;
            }


            return x;
        }


        private int[] lookup_ = new int[0];
        void generateLookup(int N)
        {
            lookup_ = new int[N / 2];

            int NoBitRev = (int)Math.Ceiling(Math.Log(N) / Math.Log(2)); //-- Find the number of bits to reverse.
            string binaryIndex_str;
            string binaryInverse_str;

            for (int i = 0; i < N / 2; i++)
            {
                binaryIndex_str = Convert.ToString(i, 2).PadLeft(NoBitRev, '0');
                binaryInverse_str = String.Empty;
                for (int j = 0; j < NoBitRev; j++)
                    binaryInverse_str += binaryIndex_str[NoBitRev - 1 - j];

                int inv = Convert.ToInt32(binaryInverse_str, 2);

                if (i == inv)
                {
                    lookup_[i] = i;
                    continue;
                }

                lookup_[i] = inv;
            }
        }

        Complex[] InPlace_bitRev(Complex[] inp)
        {
            int length = inp.Length;
            int NoBitRev = (int)Math.Ceiling(Math.Log(length) / Math.Log(2)); //Find the number of bits to reverse using

            int inv = 0;
            Complex tmp1 = 0;
            Complex tmp2 = 0;

            string bRep = String.Empty;
            string bRes = String.Empty;

            for (int i = 0; i < length / 2; i++)
            {
                inv = lookup_[i];

                if (inv == i)
                    continue;


                if (inv > i)
                {
                    tmp1 = inp[i];
                    tmp2 = inp[inv];
                    inp[i] = tmp2;
                    inp[inv] = tmp1;
                }
            }

            return inp;
        }
        #endregion

        #endregion
    }
}
