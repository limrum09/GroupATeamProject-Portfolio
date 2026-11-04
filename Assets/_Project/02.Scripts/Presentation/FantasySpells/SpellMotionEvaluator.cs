using UnityEngine;

/// <summary>60fps 기준으로 통일한 스펠 시각 동작 계산</summary>
public static class SpellMotionEvaluator
{
    /// <summary>동작별 누적 상태</summary>
    public struct State
    {
        public Vector3 Rotation;
        public float Power;
        public float ScaleFactor;
        public bool Initialized;
    }

    /// <summary>원본 동작의 한 고정 프레임 적용</summary>
    /// <param name="motion">시각 동작 설정</param>
    /// <param name="binding">적용 대상</param>
    /// <param name="target">이동 동작의 명시적 목표</param>
    /// <param name="state">누적 상태</param>
    /// <param name="time">전체 재생 시간</param>
    public static void Step(SpellMotion motion, Transform binding, Transform target, ref State state, float time)
    {
        float age = time - motion.Birth;
        if (age <= 0f || time >= motion.Death) return;
        float dt = BakedSpellDefinition.StepSeconds;
        if (!state.Initialized)
        {
            state.Power = motion.Power;
            state.ScaleFactor = 1f;
            state.Initialized = true;
        }

        switch (motion.Kind)
        {
            case SpellMotionKind.MoveToObject:
                if (age <= motion.Delay || age >= motion.Delay + motion.Duration) return;
                binding.position = Vector3.Lerp(binding.position, target.position, dt * motion.Power);
                if (Vector3.Distance(binding.position, target.position) > 1f)
                {
                    Quaternion facing = Quaternion.LookRotation(binding.position - target.position);
                    binding.rotation = Quaternion.Slerp(binding.rotation, facing, dt * motion.Look);
                }
                break;
            case SpellMotionKind.Translate:
                // 원본의 로컬 Translate에 월드 방향을 전달하는 동작 유지
                if (motion.Axes.z != 0f) binding.Translate(binding.forward * state.Power);
                if (motion.Axes.x != 0f) binding.Translate(binding.right * state.Power);
                if (motion.Axes.y != 0f) binding.Translate(binding.up * state.Power);
                if (motion.Delay != 0f && age > motion.Delay)
                    state.Power = Mathf.Clamp01(state.Power - dt / 10f);
                break;
            case SpellMotionKind.Rotate:
                if (age < motion.Delay) return;
                state.Rotation = Vector3.Lerp(state.Rotation, motion.Vector, dt);
                binding.rotation *= Quaternion.Euler(state.Rotation);
                break;
            case SpellMotionKind.Scale:
                if (age < motion.Delay) return;
                state.Power += dt / 20f;
                binding.localScale *= state.ScaleFactor;
                state.ScaleFactor = Mathf.Max(0f, state.ScaleFactor - dt * state.Power * 0.01f);
                break;
        }
    }

    /// <summary>원본 재질 소멸식의 고정 프레임 누적값</summary>
    /// <param name="age">생성 이후 경과 시간</param>
    /// <param name="delay">소멸 대기 시간</param>
    /// <param name="factor">원본 소멸 계수</param>
    /// <returns>0에서 1 사이의 소멸값</returns>
    public static float Dissolve(float age, float delay, float factor)
    {
        if (age <= delay || factor <= 0f) return 0f;
        int ageFrame = Mathf.FloorToInt(age / BakedSpellDefinition.StepSeconds + 0.0001f);
        int delayFrame = Mathf.FloorToInt(delay / BakedSpellDefinition.StepSeconds + 0.0001f);
        int steps = Mathf.Max(0, ageFrame - delayFrame);
        double alpha = BakedSpellDefinition.StepSeconds / 50.0;
        double sum = factor * (steps - (1.0 - System.Math.Pow(1.0 - alpha, steps)) / alpha);
        return Mathf.Clamp01((float)sum);
    }
}
