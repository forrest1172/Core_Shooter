using UnityEngine;

public static class AttachUtils
{
    // Align weaponRoot so that gripPoint matches socket (position + rotation).
    // Works even if you don't know offsets.
    public static void AlignGripToSocket(Transform weaponRoot, Transform gripPoint, Transform socket)
    {
        if (weaponRoot == null || gripPoint == null || socket == null) return;

        // Rotation: socketRot * inverse(gripLocalRot)
        Quaternion targetRot = socket.rotation * Quaternion.Inverse(gripPoint.localRotation);
        weaponRoot.rotation = targetRot;

        // Position: move root so grip world pos == socket world pos
        Vector3 gripWorldPos = weaponRoot.TransformPoint(gripPoint.localPosition);
        weaponRoot.position += (socket.position - gripWorldPos);
    }
}
