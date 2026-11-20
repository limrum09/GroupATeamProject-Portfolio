using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Override 클립을 교체할 Animator 상태와 원본 클립 쌍
/// </summary>
public readonly struct AnimatorClipSlot
{
    public string State { get; }
    public AnimationClip PlaceholderClip { get; }

    /// <summary>
    /// 상태와 원본 클립 쌍 구성
    /// </summary>
    /// <param name="state">Animator 상태의 전체 경로 이름</param>
    /// <param name="placeholderClip">상태에 연결된 원본 클립</param>
    public AnimatorClipSlot(string state, AnimationClip placeholderClip)
    {
        State = state;
        PlaceholderClip = placeholderClip;
    }
}

/// <summary>
/// 하나 이상의 Animator 상태에 모션을 교체하며 재생
/// </summary>
// 2개 이상이면 재생 중이 아닌 상태에만 클립을 교체하여 이전 모션과 섞어서 전환
// 1개면 같은 상태에서 매번 처음부터 즉시 재생
public class AnimatorClipSlotRotation
{
    readonly Animator _animator;
    readonly AnimatorOverrideController _overrideController;
    readonly AnimationClip[] _placeholders;
    readonly AnimationClip[] _appliedClips;
    readonly int[] _stateHashes;

    int _currentSlot = -1;

    /// <summary>
    /// 슬롯 설정 검증과 상태 해시 준비
    /// </summary>
    /// <param name="animator">Override Controller가 연결된 Animator</param>
    /// <param name="overrideController">클립을 교체할 런타임 Override Controller</param>
    /// <param name="slots">사용할 상태와 원본 클립, 1개 이상</param>
    public AnimatorClipSlotRotation(Animator animator, AnimatorOverrideController overrideController, IReadOnlyList<AnimatorClipSlot> slots)
    {
        if (animator == null || overrideController == null || slots == null)
            throw new ArgumentNullException(animator == null ? nameof(animator) : overrideController == null ? nameof(overrideController) : nameof(slots));

        if (animator.runtimeAnimatorController != overrideController)
            throw new InvalidOperationException($"{animator.name}: Override Controller 연결 이후 슬롯 준비 필요");

        if (slots.Count < 1)
            throw new InvalidOperationException($"{animator.name}: 모션 슬롯 1개 이상 필요");

        _animator = animator;
        _overrideController = overrideController;
        _placeholders = new AnimationClip[slots.Count];
        _appliedClips = new AnimationClip[slots.Count];
        _stateHashes = new int[slots.Count];

        for (int i = 0; i < slots.Count; i++)
        {
            AnimatorClipSlot slot = slots[i];

            if (string.IsNullOrWhiteSpace(slot.State) || slot.PlaceholderClip == null)
                throw new MissingReferenceException($"{animator.name}: {i}번 슬롯의 상태 이름과 원본 클립 연결 필요");

            if (slot.PlaceholderClip.isLooping)
                throw new InvalidOperationException($"{animator.name}: {i}번 슬롯 원본 클립의 Loop Time 해제 필요");

            // Override는 클립 단위로 교체되므로 슬롯마다 다른 원본 클립 필요
            if (Array.IndexOf(_placeholders, slot.PlaceholderClip) >= 0)
                throw new InvalidOperationException($"{animator.name}: 슬롯마다 서로 다른 원본 클립 필요");

            if (!HasOverride(overrideController, slot.PlaceholderClip))
                throw new InvalidOperationException($"{animator.name}: Animator Controller에서 원본 클립 '{slot.PlaceholderClip.name}'을 찾을 수 없음");

            int hash = Animator.StringToHash(slot.State);

            if (!animator.HasState(0, hash))
                throw new InvalidOperationException($"{animator.name}: Animator 상태 '{slot.State}' 확인 필요");

            _placeholders[i] = slot.PlaceholderClip;
            _stateHashes[i] = hash;
        }
    }

    /// <summary>
    /// Override Controller의 원본 클립 포함 여부
    /// </summary>
    /// <param name="controller">확인할 Override Controller</param>
    /// <param name="placeholder">찾을 원본 클립</param>
    /// <returns>원본 클립으로 등록되어 있으면 true</returns>
    public static bool HasOverride(AnimatorOverrideController controller, AnimationClip placeholder)
    {
        List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(controller.overridesCount);

        controller.GetOverrides(overrides);

        foreach (KeyValuePair<AnimationClip, AnimationClip> entry in overrides)
        {
            if (entry.Key == placeholder)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 다음 슬롯에 클립 적용 후 재생
    /// </summary>
    /// <param name="clip">재생할 모션 클립</param>
    /// <param name="normalizedTime">시작 위치, 0부터 1</param>
    /// <param name="blendTime">이전 모션과 섞는 시간(초), 0이거나 슬롯이 1개면 즉시 재생</param>
    /// <returns>재생을 시작한 상태의 전체 경로 해시</returns>
    public int Play(AnimationClip clip, float normalizedTime, float blendTime)
    {
        if (clip == null)
            throw new ArgumentNullException(nameof(clip));

        int slot = (_currentSlot + 1) % _stateHashes.Length;

        // 직전 슬롯은 섞이는 동안 유지하고 다음 슬롯만 교체
        if (_appliedClips[slot] != clip)
        {
            _overrideController[_placeholders[slot]] = clip;
            _appliedClips[slot] = clip;
        }

        // 슬롯 1개는 재생 중인 상태의 클립을 교체하므로 섞지 않고 처음부터 재생
        if (blendTime > 0f && _stateHashes.Length > 1)
            _animator.CrossFadeInFixedTime(_stateHashes[slot], blendTime, 0, normalizedTime * clip.length);
        else
            _animator.Play(_stateHashes[slot], 0, normalizedTime);

        _currentSlot = slot;

        return _stateHashes[slot];
    }
}
