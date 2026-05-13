using System.Collections.Generic;
using UnityEngine;

namespace Wonjeong.Utils 
{
    public static class CoroutineData
    {
        #region 변수

        private readonly static Dictionary<float, WaitForSeconds> DicWaitForSeconds = new Dictionary<float, WaitForSeconds>();
        public readonly static WaitForEndOfFrame WaitForEndOfFrame = new WaitForEndOfFrame();

        #endregion

        /// <summary> WaitForSeconds 반환 (재사용을 위해 하나의 Dictionary에서 계속 반환해줌) </summary>
        public static WaitForSeconds GetWaitForSeconds(float seconds)
        {
            if (!DicWaitForSeconds.ContainsKey(seconds))
            {
                DicWaitForSeconds.Add(seconds, new WaitForSeconds(seconds));
            }
            
            return DicWaitForSeconds[seconds];
        }
    }
}