using System.Collections.Generic;
using UnityEngine;

namespace Wonjeong.Utils 
{
    public static class CoroutineData
    {
        #region 변수

        private static readonly Dictionary<float, WaitForSeconds> DicWaitForSeconds = new Dictionary<float, WaitForSeconds>();
        
        public static readonly WaitForEndOfFrame WaitForEndOfFrame = new WaitForEndOfFrame();

        #endregion

        /// <summary>
        /// WaitForSeconds 반환 (재사용을 위해 하나의 Dictionary에서 계속 반환해줌)
        /// </summary>
        /// <param name="seconds">찾는 시간 초</param>
        /// <returns></returns>
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