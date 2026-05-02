import Foundation

class StreamingManager {

    private var tcpSocket: CFSocket?
    private var udpSocket: CFSocket?
    private var tcpIP: String = ""
    private var tcpPort: Int = 0
    private var udpIP: String = ""
    private var udpPort: Int = 0

    func connect(toIP ip: String, tcpPort: Int, udpPort: Int) throws {
        self.tcpIP = ip
        self.tcpPort = tcpPort
        self.udpIP = ip
        self.udpPort = udpPort

        // Create TCP socket
        try createTCPSocket()

        // Create UDP socket
        try createUDPSocket()

        // Listen for H264 encoded data
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(onH264DataReady(_:)),
            name: NSNotification.Name("H264DataReady"),
            object: nil
        )
    }

    func disconnect() {
        NotificationCenter.default.removeObserver(self)

        if let tcpSocket = tcpSocket {
            CFSocketInvalidate(tcpSocket)
        }

        if let udpSocket = udpSocket {
            CFSocketInvalidate(udpSocket)
        }

        tcpSocket = nil
        udpSocket = nil
    }

    func sendCommand(_ command: String) throws {
        guard let tcpSocket = tcpSocket else {
            throw NSError(domain: "TCP", code: -1, userInfo: [NSLocalizedDescriptionKey: "TCP socket not connected"])
        }

        let commandData = command.data(using: .utf8)!

        var address = sockaddr_in()
        address.sin_family = sa_family_t(AF_INET)
        address.sin_port = in_port_t(tcpPort).bigEndian
        address.sin_addr.s_addr = inet_addr(tcpIP)

        let addressData = NSData(bytes: &address, length: MemoryLayout<sockaddr_in>.size)
        CFSocketSendData(tcpSocket, addressData as CFData, commandData as CFData, 0)
    }

    @objc private func onH264DataReady(_ notification: NSNotification) {
        guard let data = notification.object as? Data else { return }
        sendUDPData(data)
    }

    private func sendUDPData(_ data: Data) {
        guard let udpSocket = udpSocket else { return }

        var address = sockaddr_in()
        address.sin_family = sa_family_t(AF_INET)
        address.sin_port = in_port_t(udpPort).bigEndian
        address.sin_addr.s_addr = inet_addr(udpIP)

        let addressData = NSData(bytes: &address, length: MemoryLayout<sockaddr_in>.size)
        CFSocketSendData(udpSocket, addressData as CFData, data as CFData, 0)
    }

    private func createTCPSocket() throws {
        var context = CFSocketContext()
        tcpSocket = CFSocketCreate(
            kCFAllocatorDefault,
            PF_INET,
            SOCK_STREAM,
            IPPROTO_TCP,
            CFSocketCallBackType.connectCallBack.rawValue,
            { _, _, _, _, _ in },
            &context
        )

        guard let socket = tcpSocket else {
            throw NSError(domain: "Socket", code: -1, userInfo: [NSLocalizedDescriptionKey: "Failed to create TCP socket"])
        }

        var address = sockaddr_in()
        address.sin_family = sa_family_t(AF_INET)
        address.sin_port = in_port_t(tcpPort).bigEndian
        address.sin_addr.s_addr = inet_addr(tcpIP)

        let addressData = NSData(bytes: &address, length: MemoryLayout<sockaddr_in>.size)
        CFSocketConnectToAddress(socket, addressData as CFData, 0)
    }

    private func createUDPSocket() throws {
        var context = CFSocketContext()
        udpSocket = CFSocketCreate(
            kCFAllocatorDefault,
            PF_INET,
            SOCK_DGRAM,
            IPPROTO_UDP,
            0,
            nil,
            &context
        )

        guard udpSocket != nil else {
            throw NSError(domain: "Socket", code: -1, userInfo: [NSLocalizedDescriptionKey: "Failed to create UDP socket"])
        }
    }
}
