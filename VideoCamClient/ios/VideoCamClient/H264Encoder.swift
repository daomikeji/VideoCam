import Foundation
import AVFoundation
import VideoToolbox

class H264Encoder {

    private var compressionSession: VTCompressionSession?
    private let frameRate: Int32 = 30
    private let bitRate: Int = 5000000 // 5 Mbps

    init() {
        setupCompressionSession()
    }

    private func setupCompressionSession() {
        let width: Int32 = 1280
        let height: Int32 = 720

        let status = VTCompressionSessionCreate(
            allocator: kCFAllocatorDefault,
            width: width,
            height: height,
            codecType: kCMVideoCodecType_H264,
            encoderSpecification: nil,
            imageBufferAttributes: nil,
            compressedDataAllocator: kCFAllocatorDefault,
            completionHandler: { [weak self] (status, flags, sampleBuffer) in
                if status == noErr, let sampleBuffer = sampleBuffer {
                    self?.handleEncodedFrame(sampleBuffer)
                }
            },
            refcon: nil,
            compressionSessionOut: &compressionSession
        )

        guard status == noErr, let session = compressionSession else {
            print("Failed to create compression session")
            return
        }

        // Configure session
        VTSessionSetProperty(session, key: kVTCompressionPropertyKey_RealTime, value: kCFBooleanTrue)
        VTSessionSetProperty(session, key: kVTCompressionPropertyKey_ProfileLevel, value: kVTProfileLevel_H264_Main_AutoLevel)
        VTSessionSetProperty(session, key: kVTCompressionPropertyKey_AverageBitRate, value: NSNumber(value: bitRate))
        VTSessionSetProperty(session, key: kVTCompressionPropertyKey_ExpectedFrameRate, value: NSNumber(value: frameRate))

        VTCompressionSessionPrepareToEncodeFrames(session)
    }

    func encode(pixelBuffer: CVPixelBuffer, completion: @escaping (Data) -> Void) {
        guard let session = compressionSession else { return }

        let timestamp = CMTimeMake(value: Int64(Date().timeIntervalSince1970 * 1000), timescale: 1000)
        var flags: VTEncodeInfoFlags = []

        let status = VTCompressionSessionEncodeFrame(
            session,
            imageBuffer: pixelBuffer,
            presentationTimeStamp: timestamp,
            durationsPerMacroblock: kCMTimeInvalid,
            frameProperties: nil,
            sourceFrameRefcon: nil,
            infoFlagsOut: &flags
        )

        if status != noErr {
            print("Encode frame failed: \(status)")
        }
    }

    private func handleEncodedFrame(_ sampleBuffer: CMSampleBuffer) {
        guard let dataBuffer = CMSampleBufferGetDataBuffer(sampleBuffer) else { return }

        var length = 0
        var dataPointer: UnsafeMutablePointer<Int8>?

        CMBlockBufferGetDataPointer(dataBuffer, atOffset: 0, lengthAtOffsetOut: nil, totalLengthOut: &length, dataPointerOut: &dataPointer)

        guard let dataPointer = dataPointer else { return }

        let data = Data(bytes: dataPointer, count: length)

        // Post notification with encoded data
        NotificationCenter.default.post(name: NSNotification.Name("H264DataReady"), object: data)
    }

    deinit {
        if let session = compressionSession {
            VTCompressionSessionCompleteFrames(session, untilPresentationTimeStamp: CMTime.invalid)
            VTCompressionSessionInvalidate(session)
        }
    }
}
