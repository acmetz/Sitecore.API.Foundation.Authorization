sed -i '/<<<<<<< HEAD/d' Directory.Build.props
sed -i '/<WarningsNotAsErrors>NU1603;NU1902;NU1903<\/WarningsNotAsErrors>/d' Directory.Build.props
sed -i '/=======/d' Directory.Build.props
sed -i '/>>>>>>> origin\/main/d' Directory.Build.props
