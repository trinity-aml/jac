#!/usr/bin/bash
DEST="/home"
REPO="https://github.com/immisterio/jac.git"
# Become root
# sudo su -
apt-get update && apt-get install -y wget git
# Install .NET
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh && chmod 755 dotnet-install.sh && ./dotnet-install.sh
echo "export DOTNET_ROOT=\$HOME/.dotnet" >> ~/.bashrc
echo "export PATH=\$PATH:\$HOME/.dotnet:\$HOME/.dotnet/tools" >> ~/.bashrc
source ~/.bashrc
# Clone repo and build
cd $DEST && rm -rf jac && git clone $REPO && cd jac
dotnet build -c Release -o bin
dotnet publish -c Release --self-contained --runtime linux-x64 -o bin
# Create service
echo ""
echo "Install service to /etc/systemd/system/jac.service ..."
touch /etc/systemd/system/jac.service && chmod 664 /etc/systemd/system/jac.service
cat <<EOF > /etc/systemd/system/jac.service
[Unit]
Description=JacRed
Wants=network.target
After=network.target

[Service]
WorkingDirectory=$DEST/jac
ExecStart=$DEST/jac/bin/JacRed
#ExecReload=/bin/kill -s HUP $MAINPID
#ExecStop=/bin/kill -s QUIT $MAINPID
Restart=always

[Install]
WantedBy=multi-user.target
EOF
# Enable service
systemctl daemon-reload
systemctl enable jac
# Configure JacRed
mkdir -p $DEST/jac/cache/html
cat <<EOF > $DEST/jac/init.conf
{
  "timeoutSeconds": 5,
  "htmlCacheToMinutes": 1,
  "magnetCacheToMinutes": 2,
  "apikey": "",
  "Rutor": {
    "host": "http://rutor.info",
    "enable": true,
    "useproxy": false
  },
  "TorrentBy": {
    "host": "http://torrent.by",
    "enable": true,
    "useproxy": false
  },
  "Kinozal": {
    "host": "http://kinozal.tv",
    "enable": true,
    "useproxy": false
  },
  "NNMClub": {
    "host": "https://nnmclub.to",
    "enable": true,
    "useproxy": false
  },
  "Bitru": {
    "host": "https://bitru.org",
    "enable": true,
    "useproxy": false
  },
  "Toloka": {
    "host": "https://toloka.to",
    "enable": false,
    "login": {
      "u": "user",
      "p": "passwd"
    }
  },
  "Rutracker": {
    "host": "https://rutracker.net",
    "enable": false,
    "login": {
      "u": "user",
      "p": "passwd"
    }
  },
  "Underverse": {
    "host": "https://underver.se",
    "enable": false,
    "login": {
      "u": "user",
      "p": "passwd"
    }
  },
  "proxy": {
    "useAuth": false,
    "BypassOnLocal": false,
    "username": "",
    "password": "",
    "list": [
      "ip:port",
      "socks5://ip:port"
 ]
  }
}
EOF
systemctl start jac
# Note
echo ""
echo "Please check / edit $DEST/jac/init.conf params and configure it"
echo ""
echo "Then [re]start it as systemctl [re]start jac"
echo ""
echo "Have fun!"
