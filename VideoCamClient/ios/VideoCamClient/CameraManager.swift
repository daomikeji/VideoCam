import Foundation
import AVFoundation

class CameraManager: NSObject, AVCaptureVideoDataOutputSampleBufferDelegate {

    private var captureSession: AVCaptureSession?
    private var videoOutput: AVCaptureVideoDataOutput?
    private var currentCamera: AVCaptureDevice?
    private var isFrontCamera = false
    private var h264Encoder: H264Encoder?

    override init() {
        super.init()
        self.h264Encoder = H264Encoder()
    }

    func startCapture() throws {
        let captureSession = AVCaptureSession()
        captureSession.sessionPreset = .high

        let camera = try getCamera(isFront: isFrontCamera)
        currentCamera = camera

        let input = try AVCaptureDeviceInput(device: camera)
        captureSession.addInput(input)

        let videoOutput = AVCaptureVideoDataOutput()
        videoOutput.setSampleBufferDelegate(self, queue: DispatchQueue(label: "VideoCapture"))
        captureSession.addOutput(videoOutput)

        self.videoOutput = videoOutput
        self.captureSession = captureSession

        DispatchQueue.main.async {
            captureSession.startRunning()
        }
    }

    func stopCapture() {
        DispatchQueue.main.async {
            self.captureSession?.stopRunning()
            self.captureSession = nil
        }
    }

    func switchCamera(toFront: Bool) throws {
        stopCapture()
        isFrontCamera = toFront
        try startCapture()
    }

    func toggleFlash(on: Bool) throws {
        guard let camera = currentCamera else { return }
        try camera.lockForConfiguration()

        if on && camera.hasTorch {
            do {
                try camera.setTorchModeOnWithLevel(1.0)
            } catch {
                camera.torchMode = .on
            }
        } else {
            camera.torchMode = .off
        }

        camera.unlockForConfiguration()
    }

    private func getCamera(isFront: Bool) throws -> AVCaptureDevice {
        if #available(iOS 13.0, *) {
            let discoverySession = AVCaptureDevice.DiscoverySession(
                deviceTypes: [.builtInWideAngleCamera],
                mediaType: .video,
                position: isFront ? .front : .back
            )
            guard let camera = discoverySession.devices.first else {
                throw NSError(domain: "CameraError", code: -1, userInfo: [NSLocalizedDescriptionKey: "Camera not found"])
            }
            return camera
        } else {
            let cameras = AVCaptureDevice.devices(for: .video)
            guard let camera = cameras.first(where: { $0.position == (isFront ? .front : .back) }) else {
                throw NSError(domain: "CameraError", code: -1, userInfo: [NSLocalizedDescriptionKey: "Camera not found"])
            }
            return camera
        }
    }

    // MARK: - AVCaptureVideoDataOutputSampleBufferDelegate
    func captureOutput(_ output: AVCaptureOutput, didDrop sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        // Handle dropped frames
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        guard let videoBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }

        // Encode to H.264
        h264Encoder?.encode(pixelBuffer: videoBuffer) { [weak self] encodedData in
            self?.sendEncodedData(encodedData)
        }
    }

    private func sendEncodedData(_ data: Data) {
        // Send data through streaming manager
        NotificationCenter.default.post(name: NSNotification.Name("H264DataReady"), object: data)
    }
}
